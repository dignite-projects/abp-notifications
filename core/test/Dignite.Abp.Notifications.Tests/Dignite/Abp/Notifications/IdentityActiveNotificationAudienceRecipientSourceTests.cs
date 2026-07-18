using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Identity;
using NSubstitute;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Xunit;

namespace Dignite.Abp.Notifications;

public class IdentityActiveNotificationAudienceRecipientSourceTests : DigniteAbpNotificationsTestBase
{
    [Fact]
    public async Task Pages_only_active_not_leaved_not_deleted_users_from_requested_tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var active1 = NewUser(Guid.Parse("10000000-0000-0000-0000-000000000000"), tenantId);
        var deleted = NewUser(Guid.Parse("20000000-0000-0000-0000-000000000000"), tenantId);
        SetDeleted(deleted);
        var inactive = NewUser(Guid.Parse("30000000-0000-0000-0000-000000000000"), tenantId);
        inactive.SetIsActive(false);
        var leaved = NewUser(Guid.Parse("40000000-0000-0000-0000-000000000000"), tenantId);
        leaved.SetLeaved(true);
        var active2 = NewUser(Guid.Parse("50000000-0000-0000-0000-000000000000"), tenantId);
        var otherTenant = NewUser(Guid.Parse("60000000-0000-0000-0000-000000000000"), otherTenantId);
        var repository = Substitute.For<IRepository<IdentityUser, Guid>>();
        repository.GetQueryableAsync().Returns(new[]
        {
            active2,
            otherTenant,
            leaved,
            active1,
            inactive,
            deleted
        }.AsQueryable());
        var currentTenant = new TestCurrentTenant();
        using (currentTenant.Change(tenantId, "tenant"))
        {
            var source = new IdentityActiveNotificationAudienceRecipientSource(
                repository,
                GetRequiredService<IAsyncQueryableExecuter>(),
                currentTenant);

            var firstPage = await source.GetRecipientsAsync(
                new NotificationAudienceRecipientPageRequest(
                    NotificationAudienceNames.AllActiveUsers,
                    tenantId,
                    cursor: null,
                    maxResultCount: 1));
            var secondPage = await source.GetRecipientsAsync(
                new NotificationAudienceRecipientPageRequest(
                    NotificationAudienceNames.AllActiveUsers,
                    tenantId,
                    firstPage.NextCursor,
                    maxResultCount: 1));

            firstPage.UserIds.ShouldBe(new[] { active1.Id });
            firstPage.HasMore.ShouldBeTrue();
            firstPage.NextCursor.ShouldBe(active1.Id.ToString("N"));
            secondPage.UserIds.ShouldBe(new[] { active2.Id });
            secondPage.HasMore.ShouldBeFalse();
            secondPage.NextCursor.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Rejects_ambient_tenant_mismatch()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = new TestCurrentTenant();
        var repository = Substitute.For<IRepository<IdentityUser, Guid>>();
        repository.GetQueryableAsync().Returns(Array.Empty<IdentityUser>().AsQueryable());
        using (currentTenant.Change(Guid.NewGuid(), "tenant"))
        {
            var source = new IdentityActiveNotificationAudienceRecipientSource(
                repository,
                GetRequiredService<IAsyncQueryableExecuter>(),
                currentTenant);

            await Should.ThrowAsync<InvalidOperationException>(() => source.GetRecipientsAsync(
                new NotificationAudienceRecipientPageRequest(
                    NotificationAudienceNames.AllActiveUsers,
                    tenantId,
                    cursor: null,
                    maxResultCount: 10)));
        }
    }

    private static IdentityUser NewUser(Guid id, Guid? tenantId)
    {
        return new IdentityUser(id, $"user-{id:N}", $"user-{id:N}@example.test", tenantId);
    }

    private static void SetDeleted(IdentityUser user)
    {
        var setter = typeof(IdentityUser)
            .GetProperty("IsDeleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetSetMethod(nonPublic: true)!;
        setter.Invoke(user, new object[] { true });
    }
}

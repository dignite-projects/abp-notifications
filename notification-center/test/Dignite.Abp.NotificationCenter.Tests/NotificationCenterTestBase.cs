using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.Security.Claims;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter;

public abstract class NotificationCenterTestBase : AbpIntegratedTest<NotificationCenterTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    protected virtual async Task WithUnitOfWorkAsync(Func<Task> func)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await func();
        await uow.CompleteAsync();
    }

    protected virtual IDisposable ChangeCurrentUser(Guid userId)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(AbpClaimTypes.UserId, userId.ToString()) }, "Test");
        return GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(identity));
    }
}

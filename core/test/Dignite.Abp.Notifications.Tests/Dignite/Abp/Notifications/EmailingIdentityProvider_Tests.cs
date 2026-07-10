using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Dignite.Abp.Notifications.Emailing.Identity;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Testing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class EmailingIdentityProvider_Tests
{
    [Fact]
    public async Task Resolves_email_from_identity_user()
    {
        var userId = Guid.NewGuid();
        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(userId).Returns(new IdentityUser(userId, "test-user", "test@example.com"));

        var provider = new IdentityEmailNotificationAddressProvider(repository);

        var address = await provider.GetAddressOrNullAsync(CreateContext(userId));

        address.ShouldNotBeNull();
        address!.Address.ShouldBe("test@example.com");
    }

    [Fact]
    public async Task Claims_the_notification_and_sends_nothing_when_identity_user_does_not_exist()
    {
        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(Arg.Any<Guid>()).Returns((IdentityUser?)null);

        var provider = new IdentityEmailNotificationAddressProvider(repository);

        // Last in the chain: "no address" is a claim (None), not a pass (null).
        var address = await provider.GetAddressOrNullAsync(CreateContext(Guid.NewGuid()));

        address.ShouldBeSameAs(EmailNotificationAddress.None);
        address!.Address.ShouldBeNull();
    }

    [Fact]
    public async Task Claims_the_notification_and_sends_nothing_when_identity_user_has_no_email()
    {
        var userId = Guid.NewGuid();
        var user = new IdentityUser(userId, "test-user", "test@example.com");
        user.SetEmailWithoutValidation(string.Empty, string.Empty);

        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(userId).Returns(user);

        var provider = new IdentityEmailNotificationAddressProvider(repository);

        (await provider.GetAddressOrNullAsync(CreateContext(userId))).ShouldBeSameAs(EmailNotificationAddress.None);
    }

    [Fact]
    public void Identity_provider_is_the_built_in_fallback()
    {
        new IdentityEmailNotificationAddressProvider(Substitute.For<IIdentityUserRepository>())
            .Order.ShouldBe(EmailNotificationAddressProviderOrders.BuiltInFallback);

        EmailNotificationAddressProviderOrders.BuiltInFallback
            .ShouldBeGreaterThan(EmailNotificationAddressProviderOrders.Default);
    }

    internal static EmailNotificationAddressResolveContext CreateContext(
        Guid userId, Guid? tenantId = null, string? entityTypeName = null, string? entityId = null)
    {
        return new EmailNotificationAddressResolveContext(
            new NotificationDelivery
            {
                NotificationId = Guid.NewGuid(),
                NotificationName = "test.notification",
                Data = new MessageNotificationData("test"),
                Severity = NotificationSeverity.Info,
                CreationTime = DateTime.UtcNow,
                EntityTypeName = entityTypeName,
                EntityId = entityId
            },
            userId,
            tenantId);
    }
}

public class EmailingIdentityDependency_Tests : AbpIntegratedTest<EmailingIdentityDependencyTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    [Fact]
    public void Emailing_identity_module_contributes_the_built_in_fallback_provider()
    {
        // It no longer replaces IEmailNotificationAddressResolver; it joins the provider chain, so an application
        // provider can coexist with it rather than having to displace it.
        GetRequiredService<IEnumerable<IEmailNotificationAddressProvider>>()
            .OfType<IdentityEmailNotificationAddressProvider>()
            .ShouldHaveSingleItem();

        GetRequiredService<IEmailNotificationAddressResolver>()
            .ShouldBeOfType<DefaultEmailNotificationAddressResolver>();
    }
}

[DependsOn(
    typeof(AbpNotificationsEmailingIdentityModule),
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule)
    )]
public class EmailingIdentityDependencyTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton(Substitute.For<IIdentityUserRepository>());
    }
}

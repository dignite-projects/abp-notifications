using System;
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

public class EmailingIdentityResolver_Tests
{
    [Fact]
    public async Task Resolves_email_from_identity_user()
    {
        var userId = Guid.NewGuid();
        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(userId).Returns(new IdentityUser(userId, "test-user", "test@example.com"));

        var resolver = new IdentityEmailNotificationAddressResolver(repository);

        (await resolver.GetEmailOrNullAsync(userId)).ShouldBe("test@example.com");
    }

    [Fact]
    public async Task Returns_null_when_identity_user_does_not_exist()
    {
        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(Arg.Any<Guid>()).Returns((IdentityUser?)null);

        var resolver = new IdentityEmailNotificationAddressResolver(repository);

        (await resolver.GetEmailOrNullAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task Returns_null_when_identity_user_has_no_email()
    {
        var userId = Guid.NewGuid();
        var user = new IdentityUser(userId, "test-user", "test@example.com");
        user.SetEmailWithoutValidation(string.Empty, string.Empty);

        var repository = Substitute.For<IIdentityUserRepository>();
        repository.FindAsync(userId).Returns(user);

        var resolver = new IdentityEmailNotificationAddressResolver(repository);

        (await resolver.GetEmailOrNullAsync(userId)).ShouldBeNull();
    }
}

public class EmailingIdentityDependency_Tests : AbpIntegratedTest<EmailingIdentityDependencyTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    [Fact]
    public void Emailing_identity_module_replaces_the_null_address_resolver()
    {
        GetRequiredService<IEmailNotificationAddressResolver>()
            .ShouldBeOfType<IdentityEmailNotificationAddressResolver>();
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


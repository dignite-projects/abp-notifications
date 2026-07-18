using Dignite.Abp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterDomainSharedModule),
    typeof(AbpNotificationsModule),
    typeof(AbpDddDomainModule)
    )]
public class AbpNotificationCenterDomainModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<NotificationRetentionOptions>()
            .Validate(options =>
            {
                options.Validate();
                return true;
            })
            .ValidateOnStart();

        context.Services.AddHostedService<NotificationRetentionCleanupWorker>();
    }
}

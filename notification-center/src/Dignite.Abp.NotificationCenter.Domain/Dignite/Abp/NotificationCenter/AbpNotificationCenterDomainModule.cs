using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterDomainSharedModule),
    typeof(AbpNotificationsModule),
    typeof(AbpDddDomainModule),
    typeof(AbpBackgroundWorkersModule),
    typeof(AbpDistributedLockingAbstractionsModule)
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

        context.Services.TryAddSingleton<NotificationRetentionCleanupWorker>();
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        await context.AddBackgroundWorkerAsync<NotificationRetentionCleanupWorker>();
    }
}

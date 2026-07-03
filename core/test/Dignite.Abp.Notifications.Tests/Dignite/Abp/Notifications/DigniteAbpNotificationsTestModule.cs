using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Modularity;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpNotificationsModule),
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule)
    )]
public class DigniteAbpNotificationsTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Collector for real-time ETOs delivered through the (local) distributed event bus.
        context.Services.AddSingleton<ReceivedRealTimeNotifications>();

        // No real background job infrastructure in tests — swap in a fake we can inspect.
        context.Services.AddSingleton<FakeBackgroundJobManager>();
        context.Services.Replace(
            ServiceDescriptor.Singleton<IBackgroundJobManager>(
                sp => sp.GetRequiredService<FakeBackgroundJobManager>()));

        // Permission checks: swap the always-grant default for a fake that denies one specific permission.
        context.Services.Replace(
            ServiceDescriptor.Singleton<INotificationPermissionChecker, TestNotificationPermissionChecker>());
    }
}

using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
    )]
public class AbpNotificationCenterHttpApiModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // Make MVC discover the controllers defined in this HttpApi assembly (e.g. NotificationsController).
        // These are explicit controllers that delegate to INotificationAppService — not conventional/auto API
        // controllers, so this application-part registration (not ConventionalControllers.Create) is what's needed.
        PreConfigure<IMvcBuilder>(mvcBuilder =>
        {
            mvcBuilder.AddApplicationPartIfNotExists(typeof(AbpNotificationCenterHttpApiModule).Assembly);
        });
    }
}

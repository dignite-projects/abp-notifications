using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterApplicationContractsModule),
    typeof(AbpAspNetCoreMvcModule)
    )]
public class AbpNotificationCenterHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AbpNotificationCenterApplicationContractsModule).Assembly);
        });
    }
}

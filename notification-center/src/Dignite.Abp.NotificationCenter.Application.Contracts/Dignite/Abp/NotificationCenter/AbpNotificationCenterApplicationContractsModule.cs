using Dignite.Abp.Notifications;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterDomainSharedModule),
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpAuthorizationAbstractionsModule),
    typeof(AbpDddApplicationContractsModule)
    )]
public class AbpNotificationCenterApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpPermissionOptions>(options =>
        {
            options.DefinitionProviders.Add<NotificationCenterPermissionDefinitionProvider>();
        });
    }
}

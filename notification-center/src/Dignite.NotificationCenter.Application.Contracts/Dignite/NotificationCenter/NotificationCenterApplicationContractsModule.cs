using Dignite.Abp.Notifications;
using Volo.Abp.Application;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Modularity;

namespace Dignite.NotificationCenter;

[DependsOn(
    typeof(NotificationCenterDomainSharedModule),
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpAuthorizationAbstractionsModule),
    typeof(AbpDddApplicationContractsModule)
    )]
public class NotificationCenterApplicationContractsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpPermissionOptions>(options =>
        {
            options.DefinitionProviders.Add<NotificationCenterPermissionDefinitionProvider>();
        });
    }
}

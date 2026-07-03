using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterApplicationContractsModule),
    typeof(AbpNotificationCenterDomainModule),
    typeof(AbpDddApplicationModule)
    )]
public class AbpNotificationCenterApplicationModule : AbpModule
{
}

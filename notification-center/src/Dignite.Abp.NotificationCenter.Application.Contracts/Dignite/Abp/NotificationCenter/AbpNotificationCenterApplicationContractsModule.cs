using Dignite.Abp.Notifications;
using Volo.Abp.Application;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterDomainSharedModule),
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpDddApplicationContractsModule)
    )]
public class AbpNotificationCenterApplicationContractsModule : AbpModule
{
}

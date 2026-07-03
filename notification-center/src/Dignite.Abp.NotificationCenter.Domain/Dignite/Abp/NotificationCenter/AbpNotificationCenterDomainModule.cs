using Dignite.Abp.Notifications;
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
}

using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpLocalizationModule)
    )]
public class AbpNotificationCenterDomainSharedModule : AbpModule
{
}

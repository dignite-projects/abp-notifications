using Dignite.Abp.NotificationCenter.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpLocalizationModule)
    )]
public class AbpNotificationCenterDomainSharedModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpNotificationCenterDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<NotificationCenterResource>("en")
                .AddVirtualJson("/Dignite/Abp/NotificationCenter/Localization");
        });
    }
}

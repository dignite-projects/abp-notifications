using Dignite.Abp.NotificationCenter.Web.Menus;
using Dignite.Abp.NotificationCenter.Web.Toolbar;
using Dignite.Abp.Notifications;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared.Toolbars;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation;
using Volo.Abp.VirtualFileSystem;

namespace Dignite.Abp.NotificationCenter.Web;

[DependsOn(
    typeof(AbpNotificationCenterApplicationContractsModule),
    typeof(AbpAspNetCoreMvcUiThemeSharedModule)
    )]
public class AbpNotificationCenterWebModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpNotificationCenterWebModule>();
        });

        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                NotificationCenterWebConsts.GlobalBundleName,
                bundle => bundle.AddFiles("/Dignite/Abp/NotificationCenter/Web/notification-center.css"));

            options.ScriptBundles.Configure(
                NotificationCenterWebConsts.GlobalBundleName,
                bundle => bundle.AddFiles("/Dignite/Abp/NotificationCenter/Web/notification-center.js"));
        });

        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new NotificationCenterMenuContributor());
        });

        Configure<AbpToolbarOptions>(options =>
        {
            options.Contributors.Add(new NotificationCenterToolbarContributor());
        });

        Configure<NotificationCenterWebOptions>(options =>
        {
            options.DataViewComponents[MessageDiscriminator] =
                typeof(Components.MessageNotificationData.MessageNotificationDataViewComponent);
            options.DataViewComponents[LocalizableMessageDiscriminator] =
                typeof(Components.LocalizableMessageNotificationData.LocalizableMessageNotificationDataViewComponent);
        });
    }

    private static readonly string MessageDiscriminator =
        NotificationDataTypeAttribute.GetNameOrNull(typeof(MessageNotificationData))!;

    private static readonly string LocalizableMessageDiscriminator =
        NotificationDataTypeAttribute.GetNameOrNull(typeof(LocalizableMessageNotificationData))!;
}

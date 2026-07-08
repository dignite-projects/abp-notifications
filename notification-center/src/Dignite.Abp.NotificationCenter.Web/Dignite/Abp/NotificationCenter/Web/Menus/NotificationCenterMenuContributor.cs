using System.Threading.Tasks;
using Dignite.Abp.NotificationCenter.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Volo.Abp.UI.Navigation;

namespace Dignite.Abp.NotificationCenter.Web.Menus;

public class NotificationCenterMenuContributor : IMenuContributor
{
    public virtual Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name != StandardMenus.Main)
        {
            return Task.CompletedTask;
        }

        var l = context.ServiceProvider.GetRequiredService<IStringLocalizer<NotificationCenterResource>>();

        context.Menu.AddItem(
            new ApplicationMenuItem(
                NotificationCenterWebConsts.MenuGroupName + ".Subscriptions",
                l["Menu:Notifications"],
                "/NotificationCenter/Subscriptions",
                icon: "fa fa-bell"
            )
        );

        return Task.CompletedTask;
    }
}

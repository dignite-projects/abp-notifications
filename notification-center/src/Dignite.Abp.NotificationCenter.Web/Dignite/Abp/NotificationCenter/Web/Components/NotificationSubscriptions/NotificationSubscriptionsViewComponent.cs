using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dignite.Abp.NotificationCenter.Web.Components.NotificationSubscriptions;

public class NotificationSubscriptionsViewComponent : ViewComponent
{
    protected IUserNotificationAppService UserNotificationAppService { get; }

    public NotificationSubscriptionsViewComponent(IUserNotificationAppService userNotificationAppService)
    {
        UserNotificationAppService = userNotificationAppService;
    }

    public virtual async Task<IViewComponentResult> InvokeAsync()
    {
        var result = await UserNotificationAppService.GetSubscriptionsAsync();
        return View(
            "~/Dignite/Abp/NotificationCenter/Web/Components/NotificationSubscriptions/Default.cshtml",
            result.Items.ToList());
    }
}

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Dignite.Abp.NotificationCenter.Web.Components.NotificationSubscriptions;

public class NotificationSubscriptionsViewComponent : ViewComponent
{
    protected INotificationAppService NotificationAppService { get; }

    public NotificationSubscriptionsViewComponent(INotificationAppService notificationAppService)
    {
        NotificationAppService = notificationAppService;
    }

    public virtual async Task<IViewComponentResult> InvokeAsync()
    {
        var result = await NotificationAppService.GetSubscriptionsAsync();
        return View(
            "~/Dignite/Abp/NotificationCenter/Web/Components/NotificationSubscriptions/Default.cshtml",
            result.Items.ToList());
    }
}

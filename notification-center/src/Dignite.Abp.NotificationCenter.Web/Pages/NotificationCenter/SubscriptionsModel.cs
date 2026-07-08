using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Dignite.Abp.NotificationCenter.Web.Pages.NotificationCenter;

public class SubscriptionsModel : AbpPageModel
{
    protected INotificationAppService NotificationAppService { get; }

    public List<NotificationSubscriptionDto> Subscriptions { get; set; } = new();

    public SubscriptionsModel(INotificationAppService notificationAppService)
    {
        NotificationAppService = notificationAppService;
    }

    public virtual async Task OnGetAsync()
    {
        var result = await NotificationAppService.GetSubscriptionsAsync();
        Subscriptions = result.Items.ToList();
    }

    // No OnPostToggle handler on purpose: the subscribe/unsubscribe toggles call the NotificationCenter
    // API directly via ABP's dynamic JS proxy (dignite.abp.notificationCenter.notifications.subscribe /
    // .unsubscribe), so this page only renders the current subscription state (OnGetAsync).
}

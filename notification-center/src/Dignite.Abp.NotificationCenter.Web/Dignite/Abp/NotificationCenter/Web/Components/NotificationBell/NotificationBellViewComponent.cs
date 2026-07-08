using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Dignite.Abp.NotificationCenter.Web.Components.NotificationBell;

public class NotificationBellViewComponent : ViewComponent
{
    protected INotificationAppService NotificationAppService { get; }

    protected INotificationDataTypeRegistry NotificationDataTypeRegistry { get; }

    protected NotificationCenterWebOptions WebOptions { get; }

    public NotificationBellViewComponent(
        INotificationAppService notificationAppService,
        INotificationDataTypeRegistry notificationDataTypeRegistry,
        IOptions<NotificationCenterWebOptions> webOptions)
    {
        NotificationAppService = notificationAppService;
        NotificationDataTypeRegistry = notificationDataTypeRegistry;
        WebOptions = webOptions.Value;
    }

    public virtual async Task<IViewComponentResult> InvokeAsync()
    {
        // The bell sits in the theme toolbar, which also renders on anonymous pages (e.g. the login page).
        // Querying per-user state without an authenticated user would throw — render nothing instead.
        if (User?.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        var unreadCount = await NotificationAppService.GetCountAsync(UserNotificationState.Unread);
        var recent = await NotificationAppService.GetListAsync(new GetUserNotificationListInput
        {
            MaxResultCount = 10
        });

        var items = recent.Items.Select(CreateItemViewModel).ToList();

        return View(new NotificationBellViewModel(unreadCount, items, WebOptions.SignalRHubUrl));
    }

    protected virtual NotificationBellItemViewModel CreateItemViewModel(UserNotificationDto notification)
    {
        var discriminator = notification.Data == null
            ? null
            : NotificationDataTypeRegistry.GetDiscriminatorOrNull(notification.Data.GetType());

        var url = ResolveEntityUrl(notification);

        return new NotificationBellItemViewModel(notification, discriminator, url);
    }

    protected virtual string? ResolveEntityUrl(UserNotificationDto notification)
    {
        if (notification.EntityTypeName == null || notification.EntityId == null)
        {
            return null;
        }

        return WebOptions.EntityLinkResolvers.TryGetValue(notification.EntityTypeName, out var resolve)
            ? resolve(notification.EntityId)
            : null;
    }
}

using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// REST endpoints for the current user's notification inbox + subscriptions. A thin controller that delegates
/// to <see cref="INotificationAppService"/> — the AppService owns all authorization (its class-level [Authorize])
/// and per-user scoping. Exposed under <c>/api/notifications</c>.
/// </summary>
[RemoteService(Name = "NotificationCenter")]
[Area("notification-center")]
[Route("api/notifications")]
public class NotificationsController : AbpControllerBase, INotificationAppService
{
    protected INotificationAppService NotificationAppService { get; }

    public NotificationsController(INotificationAppService notificationAppService)
    {
        NotificationAppService = notificationAppService;
    }

    [HttpGet]
    public virtual Task<PagedResultDto<UserNotificationDto>> GetListAsync(GetUserNotificationListInput input)
    {
        return NotificationAppService.GetListAsync(input);
    }

    [HttpGet]
    [Route("count")]
    public virtual Task<int> GetCountAsync(UserNotificationState? state = null)
    {
        return NotificationAppService.GetCountAsync(state);
    }

    [HttpPost]
    [Route("{notificationId}/mark-as-read")]
    public virtual Task MarkAsReadAsync(Guid notificationId)
    {
        return NotificationAppService.MarkAsReadAsync(notificationId);
    }

    [HttpPost]
    [Route("mark-all-as-read")]
    public virtual Task MarkAllAsReadAsync()
    {
        return NotificationAppService.MarkAllAsReadAsync();
    }

    [HttpDelete]
    [Route("{notificationId}")]
    public virtual Task DeleteAsync(Guid notificationId)
    {
        return NotificationAppService.DeleteAsync(notificationId);
    }

    [HttpDelete]
    public virtual Task DeleteAllAsync(UserNotificationState? state = null)
    {
        return NotificationAppService.DeleteAllAsync(state);
    }

    [HttpGet]
    [Route("subscriptions")]
    public virtual Task<ListResultDto<NotificationSubscriptionDto>> GetSubscriptionsAsync()
    {
        return NotificationAppService.GetSubscriptionsAsync();
    }

    [HttpPost]
    [Route("subscriptions")]
    public virtual Task SubscribeAsync(string notificationName)
    {
        return NotificationAppService.SubscribeAsync(notificationName);
    }

    [HttpDelete]
    [Route("subscriptions/{notificationName}")]
    public virtual Task UnsubscribeAsync(string notificationName)
    {
        return NotificationAppService.UnsubscribeAsync(notificationName);
    }

    [HttpPost]
    [Route("subscription-scopes")]
    public virtual Task SubscribeScopedAsync([FromBody] NotificationSubscriptionScopeDto input)
    {
        return NotificationAppService.SubscribeScopedAsync(input);
    }

    [HttpDelete]
    [Route("subscription-scopes")]
    public virtual Task UnsubscribeScopedAsync([FromQuery] NotificationSubscriptionScopeDto input)
    {
        return NotificationAppService.UnsubscribeScopedAsync(input);
    }
}

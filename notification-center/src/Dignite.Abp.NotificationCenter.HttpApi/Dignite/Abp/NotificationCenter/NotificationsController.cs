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
/// to <see cref="IUserNotificationAppService"/> — the AppService owns all authorization (its class-level [Authorize])
/// and per-user scoping. Exposed under <c>/api/notifications</c>.
/// </summary>
[RemoteService(Name = "NotificationCenter")]
[Area("notification-center")]
[Route("api/notifications")]
public class NotificationsController : AbpControllerBase, IUserNotificationAppService
{
    protected IUserNotificationAppService UserNotificationAppService { get; }

    public NotificationsController(IUserNotificationAppService userNotificationAppService)
    {
        UserNotificationAppService = userNotificationAppService;
    }

    [HttpGet]
    public virtual Task<PagedResultDto<UserNotificationDto>> GetListAsync(GetUserNotificationListInput input)
    {
        return UserNotificationAppService.GetListAsync(input);
    }

    [HttpGet]
    [Route("count")]
    public virtual Task<int> GetNotificationCountAsync(UserNotificationState? state = null)
    {
        return UserNotificationAppService.GetNotificationCountAsync(state);
    }

    [HttpPost]
    [Route("{notificationId}/mark-as-read")]
    public virtual Task MarkAsReadAsync(Guid notificationId)
    {
        return UserNotificationAppService.MarkAsReadAsync(notificationId);
    }

    [HttpPost]
    [Route("mark-all-as-read")]
    public virtual Task MarkAllAsReadAsync()
    {
        return UserNotificationAppService.MarkAllAsReadAsync();
    }

    [HttpDelete]
    [Route("{notificationId}")]
    public virtual Task DeleteAsync(Guid notificationId)
    {
        return UserNotificationAppService.DeleteAsync(notificationId);
    }

    [HttpDelete]
    public virtual Task DeleteAllAsync(UserNotificationState? state = null)
    {
        return UserNotificationAppService.DeleteAllAsync(state);
    }

    [HttpGet]
    [Route("subscriptions")]
    public virtual Task<ListResultDto<NotificationSubscriptionDto>> GetSubscriptionsAsync()
    {
        return UserNotificationAppService.GetSubscriptionsAsync();
    }

    [HttpPost]
    [Route("subscriptions")]
    public virtual Task SubscribeAsync(string notificationName)
    {
        return UserNotificationAppService.SubscribeAsync(notificationName);
    }

    [HttpDelete]
    [Route("subscriptions/{notificationName}")]
    public virtual Task UnsubscribeAsync(string notificationName)
    {
        return UserNotificationAppService.UnsubscribeAsync(notificationName);
    }

    [HttpPost]
    [Route("subscription-scopes")]
    public virtual Task SubscribeScopedAsync([FromBody] NotificationSubscriptionScopeDto input)
    {
        return UserNotificationAppService.SubscribeScopedAsync(input);
    }

    [HttpDelete]
    [Route("subscription-scopes")]
    public virtual Task UnsubscribeScopedAsync([FromQuery] NotificationSubscriptionScopeDto input)
    {
        return UserNotificationAppService.UnsubscribeScopedAsync(input);
    }
}

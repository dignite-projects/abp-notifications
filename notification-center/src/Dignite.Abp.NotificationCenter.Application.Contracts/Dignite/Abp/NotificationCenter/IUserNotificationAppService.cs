using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Headless inbox + subscription API for the current user.</summary>
public interface IUserNotificationAppService : IApplicationService
{
    Task<PagedResultDto<UserNotificationDto>> GetListAsync(GetUserNotificationListInput input);

    /// <summary>Gets the current user's notification count, optionally filtered by state.</summary>
    Task<int> GetNotificationCountAsync(UserNotificationState? state = null);

    Task MarkAsReadAsync(Guid notificationId);

    Task MarkAllAsReadAsync();

    Task DeleteAsync(Guid notificationId);

    Task DeleteAllAsync(UserNotificationState? state = null);

    Task<ListResultDto<NotificationSubscriptionDto>> GetSubscriptionsAsync();

    Task SubscribeAsync(string notificationName);

    Task UnsubscribeAsync(string notificationName);

    /// <summary>Subscribes the current user to exactly the supplied definition-wide or entity scope.</summary>
    Task SubscribeScopedAsync(NotificationSubscriptionScopeDto input);

    /// <summary>Removes exactly the supplied definition-wide or entity scope.</summary>
    Task UnsubscribeScopedAsync(NotificationSubscriptionScopeDto input);
}

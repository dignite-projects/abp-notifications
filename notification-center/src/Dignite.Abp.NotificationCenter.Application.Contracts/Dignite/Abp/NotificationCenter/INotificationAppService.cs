using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Headless inbox + subscription API for the current user.</summary>
public interface INotificationAppService : IApplicationService
{
    Task<PagedResultDto<UserNotificationDto>> GetListAsync(GetUserNotificationListInput input);

    Task<int> GetCountAsync(UserNotificationState? state = null);

    Task MarkAsReadAsync(Guid notificationId);

    Task MarkAllAsReadAsync();

    Task DeleteAsync(Guid notificationId);

    Task DeleteAllAsync(UserNotificationState? state = null);

    Task<ListResultDto<NotificationSubscriptionDto>> GetSubscriptionsAsync();

    Task SubscribeAsync(string notificationName);

    Task UnsubscribeAsync(string notificationName);
}

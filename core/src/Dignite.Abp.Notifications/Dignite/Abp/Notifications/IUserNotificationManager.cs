using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface IUserNotificationManager
{
    Task<List<UserNotificationWithNotification>> GetUserNotificationsAsync(
        Guid userId,
        UserNotificationState? state = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        DateTime? startDate = null,
        DateTime? endDate = null);

    Task<int> GetUserNotificationCountAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null);

    Task UpdateUserNotificationStateAsync(Guid userId, Guid notificationId, UserNotificationState state);

    Task UpdateAllUserNotificationStatesAsync(Guid userId, UserNotificationState state);

    Task DeleteUserNotificationAsync(Guid userId, Guid notificationId);

    Task DeleteAllUserNotificationsAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null);
}

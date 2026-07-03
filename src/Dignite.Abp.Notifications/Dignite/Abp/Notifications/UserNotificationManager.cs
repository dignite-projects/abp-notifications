using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Transient — not singleton as in the reference implementation. It depends on the request-scoped
/// <see cref="INotificationStore"/> (which, in NotificationCenter mode, holds a repository / DbContext),
/// so a singleton would capture scoped services and break under concurrency.
/// </summary>
public class UserNotificationManager : IUserNotificationManager, ITransientDependency
{
    protected INotificationStore Store { get; }

    public UserNotificationManager(INotificationStore store)
    {
        Store = store;
    }

    public virtual Task<List<UserNotificationWithNotification>> GetUserNotificationsAsync(
        Guid userId,
        UserNotificationState? state = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        return Store.GetUserNotificationsAsync(userId, state, skipCount, maxResultCount, startDate, endDate);
    }

    public virtual Task<int> GetUserNotificationCountAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        return Store.GetUserNotificationCountAsync(userId, state, startDate, endDate);
    }

    public virtual Task UpdateUserNotificationStateAsync(Guid userId, Guid notificationId, UserNotificationState state)
    {
        return Store.UpdateUserNotificationStateAsync(userId, notificationId, state);
    }

    public virtual Task UpdateAllUserNotificationStatesAsync(Guid userId, UserNotificationState state)
    {
        return Store.UpdateAllUserNotificationStatesAsync(userId, state);
    }

    public virtual Task DeleteUserNotificationAsync(Guid userId, Guid notificationId)
    {
        return Store.DeleteUserNotificationAsync(userId, notificationId);
    }

    public virtual Task DeleteAllUserNotificationsAsync(
        Guid userId, UserNotificationState? state = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        return Store.DeleteAllUserNotificationsAsync(userId, state, startDate, endDate);
    }
}

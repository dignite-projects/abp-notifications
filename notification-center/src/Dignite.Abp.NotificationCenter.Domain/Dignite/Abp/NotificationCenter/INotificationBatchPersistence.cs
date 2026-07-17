using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Provider-specific persistence boundary for one already-bounded group of inbox entities.
/// </summary>
public interface INotificationBatchPersistence
{
    Task InsertAsync(
        IReadOnlyCollection<UserNotification> userNotifications,
        CancellationToken cancellationToken = default);
}

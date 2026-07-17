using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Provider-neutral batch writer used by MongoDB. EF Core replaces it so saved entities can be detached after
/// each provider write instead of accumulating in the change tracker.
/// </summary>
[ExposeServices(typeof(INotificationBatchPersistence))]
public class NotificationBatchPersistence : INotificationBatchPersistence, ITransientDependency
{
    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }

    public NotificationBatchPersistence(
        IRepository<UserNotification, Guid> userNotificationRepository)
    {
        UserNotificationRepository = userNotificationRepository;
    }

    public virtual Task InsertAsync(
        IReadOnlyCollection<UserNotification> userNotifications,
        CancellationToken cancellationToken = default)
    {
        return UserNotificationRepository.InsertManyAsync(
            userNotifications,
            autoSave: true,
            cancellationToken: cancellationToken);
    }
}

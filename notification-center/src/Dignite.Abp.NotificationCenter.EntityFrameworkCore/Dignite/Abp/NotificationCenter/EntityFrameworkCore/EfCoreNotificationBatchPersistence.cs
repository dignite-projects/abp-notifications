using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;

namespace Dignite.Abp.NotificationCenter.EntityFrameworkCore;

/// <summary>
/// Flushes each bounded inbox group through the ambient EF Core context, then detaches only that completed group
/// so the change tracker stays bounded. An ambient transactional unit of work still controls commit/rollback.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(INotificationBatchPersistence))]
public class EfCoreNotificationBatchPersistence : INotificationBatchPersistence, ITransientDependency
{
    protected IRepository<UserNotification, Guid> UserNotificationRepository { get; }

    public EfCoreNotificationBatchPersistence(
        IRepository<UserNotification, Guid> userNotificationRepository)
    {
        UserNotificationRepository = userNotificationRepository;
    }

    public virtual async Task InsertAsync(
        IReadOnlyCollection<UserNotification> userNotifications,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await UserNotificationRepository.InsertManyAsync(
                userNotifications,
                autoSave: true,
                cancellationToken: cancellationToken);
        }
        finally
        {
            var dbContext = await ((IEfCoreRepository<UserNotification, Guid>)UserNotificationRepository)
                .GetDbContextAsync();
            foreach (var userNotification in userNotifications)
            {
                dbContext.Entry(userNotification).State = EntityState.Detached;
            }
        }
    }
}

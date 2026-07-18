using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.NotificationCenter;

public interface INotificationRetentionCleanupService
{
    Task<NotificationRetentionCleanupResult> CleanupAsync(
        NotificationRetentionCleanupRequest? request = null,
        CancellationToken cancellationToken = default);
}

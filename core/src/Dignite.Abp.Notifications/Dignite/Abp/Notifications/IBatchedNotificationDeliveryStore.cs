using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>Optional bounded scheduling capability for delivery-state stores.</summary>
public interface IBatchedNotificationDeliveryStore
{
    Task EnsureCreatedAsync(
        IReadOnlyCollection<NotificationDeliveryWorkEto> workItems,
        CancellationToken cancellationToken = default);
}

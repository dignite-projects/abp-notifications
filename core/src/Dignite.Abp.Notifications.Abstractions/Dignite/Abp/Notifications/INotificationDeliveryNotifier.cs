using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Preferred notifier contract for per-recipient/channel delivery. The stable idempotency key is carried on the
/// work item so providers that support downstream deduplication can forward it.
/// </summary>
public interface INotificationDeliveryNotifier : INotificationNotifier
{
    Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequestedEto workItem);
}

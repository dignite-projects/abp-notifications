using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Entry point business code calls to publish a notification. Small explicit fan-outs distribute inline;
/// larger ones go to a background job (threshold configurable via <see cref="NotificationOptions"/>).
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null);
}

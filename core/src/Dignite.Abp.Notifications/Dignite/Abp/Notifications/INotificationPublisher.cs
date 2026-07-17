using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Entry point business code calls to publish a notification. Small explicit fan-outs distribute inline;
/// larger ones go to a background job (threshold configurable via <see cref="NotificationOptions"/>).
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Publishes a notification to subscribers or to an explicit set of users. Opted-in payload and entity
    /// definition contracts are validated before background enqueue, persistence, or event publication.
    /// </summary>
    /// <param name="notificationName">The registered notification name.</param>
    /// <param name="data">The payload, optional unless the definition declares a payload discriminator.</param>
    /// <param name="entityIdentifier">The stable related-entity identity, subject to the definition's entity contract.</param>
    /// <param name="severity">The notification severity.</param>
    /// <param name="userIds">
    /// The recipients. <see langword="null"/> resolves recipients from subscriptions; an empty array is an
    /// intentional no-op; a non-empty array targets those users explicitly. Duplicate IDs are removed before
    /// the inline/background threshold is evaluated and before distribution. Explicit and subscription-derived
    /// recipients must satisfy the notification definition's feature and permission requirements.
    /// </param>
    /// <param name="excludedUserIds">Optional user IDs to remove from the resolved recipient set.</param>
    Task PublishAsync(
        string notificationName,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? userIds = null,
        Guid[]? excludedUserIds = null);

    /// <summary>
    /// Publishes a trusted-system notification to explicit users without evaluating the notification definition's
    /// feature or permission requirements. The bypass is logged and cannot be used for subscription resolution;
    /// payload and entity definition contracts are still validated before any side effect.
    /// </summary>
    /// <param name="notificationName">The registered notification name.</param>
    /// <param name="userIds">The explicit recipients. An empty array is an intentional no-op.</param>
    /// <param name="data">The optional notification payload.</param>
    /// <param name="entityIdentifier">The optional stable identifier of the related entity.</param>
    /// <param name="severity">The notification severity.</param>
    /// <param name="excludedUserIds">Optional user IDs to remove before delivery.</param>
    Task PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
        string notificationName,
        Guid[] userIds,
        NotificationData? data = null,
        NotificationEntityIdentifier? entityIdentifier = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        Guid[]? excludedUserIds = null);
}

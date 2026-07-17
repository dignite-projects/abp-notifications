using System;
using Volo.Abp.EventBus;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Legacy aggregate delivery event retained for source and wire compatibility. New producers publish
/// <see cref="NotificationDeliveryWorkEto"/>; the reliable processor adapts one work item back to a singleton instance
/// of this type for existing notifier implementations. An aggregate event from an old producer does not gain
/// per-recipient state or partial-progress reliability merely because a newer consumer can deserialize it.
/// </summary>
[EventName("Dignite.Abp.Notifications.NotificationDelivery")]
[Serializable]
public class NotificationDeliveryEto : IEventDataMayHaveTenantId
{
    /// <summary>
    /// Stable per-recipient/channel delivery identity when this instance was adapted from the reliable work-item
    /// pipeline. Null for legacy aggregate events produced by older applications.
    /// </summary>
    public Guid? DeliveryId { get; set; }

    /// <summary>Stable downstream idempotency key; null on legacy aggregate events.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Single recipient on reliable adapted events; null on legacy aggregate events.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Single channel on reliable adapted events; null on legacy aggregate events.</summary>
    public string? Channel { get; set; }

    public Guid NotificationId { get; set; }

    public string NotificationName { get; set; } = default!;

    public NotificationData? Data { get; set; }

    public NotificationSeverity Severity { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid[] UserIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Stable name of the entity type this notification is about, e.g. <c>"Demo.Order"</c> — never a CLR type name.
    /// Lets a notifier fetch business context (see <c>IEmailNotificationAddressResolver</c>) and lets a client build
    /// an entity link without re-fetching. Null when the notification is not about a specific entity.
    /// </summary>
    public string? EntityTypeName { get; set; }

    /// <summary>The entity's identifier, rendered as a string. Null when <see cref="EntityTypeName"/> is null.</summary>
    public string? EntityId { get; set; }

    /// <summary>Notifier channels this notification may be delivered on (by name). Null/empty = no external channel.</summary>
    public string[]? Channels { get; set; }

    /// <summary>
    /// The tenant that owns this notification, captured by <c>INotificationPublisher</c> at publish time. ABP's event
    /// bus enters this tenant before invoking a notifier, so a notifier never has to switch tenants itself. When it
    /// is null, <see cref="IEventDataMayHaveTenantId.IsMultiTenant"/> returns false and the bus leaves the ambient
    /// tenant alone — which, for an out-of-process consumer, is host.
    /// </summary>
    public Guid? TenantId { get; set; }

    public NotificationDeliveryEto()
    {
    }

    public NotificationDeliveryEto(
        Guid notificationId,
        string notificationName,
        NotificationData? data,
        NotificationSeverity severity,
        DateTime creationTime,
        Guid[] userIds)
    {
        NotificationId = notificationId;
        NotificationName = notificationName;
        Data = data;
        Severity = severity;
        CreationTime = creationTime;
        UserIds = userIds;
    }

    public bool IsMultiTenant(out Guid? tenantId)
    {
        tenantId = TenantId;
        return TenantId.HasValue;
    }
}

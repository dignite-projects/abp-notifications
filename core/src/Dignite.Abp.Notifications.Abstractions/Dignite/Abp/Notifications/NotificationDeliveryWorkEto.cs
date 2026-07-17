using System;
using Volo.Abp.EventBus;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A bounded delivery work item for exactly one tenant, notification, recipient and channel. Redelivery is safe
/// because <see cref="DeliveryId"/> and <see cref="IdempotencyKey"/> are stable for that identity.
/// </summary>
[EventName("Dignite.Abp.Notifications.NotificationDeliveryWork")]
[Serializable]
public class NotificationDeliveryWorkEto : IEventDataMayHaveTenantId
{
    public Guid DeliveryId { get; set; }

    public string IdempotencyKey { get; set; } = default!;

    public Guid NotificationId { get; set; }

    public string NotificationName { get; set; } = default!;

    public NotificationData? Data { get; set; }

    public NotificationSeverity Severity { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid UserId { get; set; }

    public string Channel { get; set; } = default!;

    public string? EntityTypeName { get; set; }

    public string? EntityId { get; set; }

    public Guid? TenantId { get; set; }

    public bool IsMultiTenant(out Guid? tenantId)
    {
        tenantId = TenantId;
        return TenantId.HasValue;
    }

    /// <summary>
    /// Adapts this work item to the original aggregate event contract. Existing custom notifiers therefore keep
    /// working, but receive a singleton recipient/channel event and are executed inside the reliable state machine.
    /// </summary>
    public NotificationDeliveryEto ToLegacyEto()
    {
        return new NotificationDeliveryEto(
            NotificationId,
            NotificationName,
            Data,
            Severity,
            CreationTime,
            new[] { UserId })
        {
            Channels = new[] { Channel },
            TenantId = TenantId,
            EntityTypeName = EntityTypeName,
            EntityId = EntityId,
            DeliveryId = DeliveryId,
            IdempotencyKey = IdempotencyKey,
            UserId = UserId,
            Channel = Channel
        };
    }
}

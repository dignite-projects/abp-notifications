using System;
using Volo.Abp.EventBus;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A best-effort delivery request for exactly one tenant, notification, recipient and channel. A channel
/// notifier delivers it once and does not persist per-recipient delivery state.
/// The event name intentionally retains its pre-stable wire value so mixed-version consumers continue to resolve
/// the same distributed event while the CLR contract name is normalized.
/// </summary>
[EventName("Dignite.Abp.Notifications.NotificationDeliveryWork")]
[Serializable]
public class NotificationDeliveryRequestedEto : IEventDataMayHaveTenantId
{
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
}

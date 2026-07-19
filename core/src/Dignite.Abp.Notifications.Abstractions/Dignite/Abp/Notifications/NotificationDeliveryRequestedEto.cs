using System;
using Volo.Abp.EventBus;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A delivery request for exactly one tenant, notification, recipient and channel. Redelivery is safe
/// because <see cref="DeliveryId"/> and <see cref="IdempotencyKey"/> are stable for that identity.
/// The event name intentionally retains its pre-stable wire value so mixed-version consumers continue to resolve
/// the same distributed event while the CLR contract name is normalized.
/// </summary>
[EventName("Dignite.Abp.Notifications.NotificationDeliveryWork")]
[Serializable]
public class NotificationDeliveryRequestedEto : IEventDataMayHaveTenantId
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

    /// <summary>The final channel-specific intent resolved by the producer.</summary>
    public NotificationDeliveryIntent Intent { get; set; }

    /// <summary>Earliest delivery time for <see cref="NotificationDeliveryIntent.Delay"/>.</summary>
    public DateTime? DeliveryNotBefore { get; set; }

    /// <summary>Stable diagnostic code explaining a producer-side suppression or delay.</summary>
    public string? PreferenceReasonCode { get; set; }

    public string? EntityTypeName { get; set; }

    public string? EntityId { get; set; }

    public Guid? TenantId { get; set; }

    public bool IsMultiTenant(out Guid? tenantId)
    {
        tenantId = TenantId;
        return TenantId.HasValue;
    }

    /// <summary>Rejects malformed producer intent before a channel consumer persists or executes the work.</summary>
    public void ValidateIntent()
    {
        var isValid = Intent switch
        {
            NotificationDeliveryIntent.Deliver => !DeliveryNotBefore.HasValue && PreferenceReasonCode == null,
            NotificationDeliveryIntent.Suppress => !DeliveryNotBefore.HasValue
                                                   && !string.IsNullOrWhiteSpace(PreferenceReasonCode),
            NotificationDeliveryIntent.Delay => DeliveryNotBefore.HasValue
                                                && !string.IsNullOrWhiteSpace(PreferenceReasonCode),
            _ => false
        };
        if (!isValid || PreferenceReasonCode?.Length > NotificationDeliveryPreferenceReasonCodes.MaxLength)
        {
            throw new InvalidOperationException($"The notification delivery intent '{Intent}' is invalid.");
        }
    }

}

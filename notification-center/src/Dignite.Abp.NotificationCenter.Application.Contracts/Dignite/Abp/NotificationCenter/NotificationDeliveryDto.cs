using System;
using Dignite.Abp.Notifications;

namespace Dignite.Abp.NotificationCenter;

public class NotificationDeliveryDto
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public Guid NotificationId { get; set; }

    public Guid UserId { get; set; }

    public string Channel { get; set; } = default!;

    public string IdempotencyKey { get; set; } = default!;

    /// <summary>The producer-resolved delivery intent. Only an explicit force-delivery operation may override a
    /// suppressed intent; an ordinary retry preserves it.</summary>
    public NotificationDeliveryIntent Intent { get; set; }

    public DateTime? DeliveryNotBefore { get; set; }

    /// <summary>Stable reason code when the producer suppressed or delayed this delivery (e.g. user opt-out).</summary>
    public string? PreferenceReasonCode { get; set; }

    public NotificationDeliveryState State { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? NextAttemptTime { get; set; }

    public DateTime? LastAttemptTime { get; set; }

    public DateTime? LeaseExpirationTime { get; set; }

    public DateTime? CompletedTime { get; set; }

    public string? LastFailureCode { get; set; }

    /// <summary>Sanitized diagnostic text; never contains exception messages or notification payload data.</summary>
    public string? LastFailureMessage { get; set; }

    public Guid? LastForceDeliveryActorId { get; set; }

    public DateTime? LastForceDeliveryTime { get; set; }

    public NotificationDeliveryState? LastForceDeliveryPreviousState { get; set; }

    /// <summary>Stable, non-sensitive audit reason for the latest explicit force-delivery override.</summary>
    public string? LastForceDeliveryReasonCode { get; set; }

    public DateTime CreationTime { get; set; }
}

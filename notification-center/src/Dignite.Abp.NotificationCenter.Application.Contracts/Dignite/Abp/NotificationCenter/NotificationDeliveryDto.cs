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

    public NotificationDeliveryState State { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? NextAttemptTime { get; set; }

    public DateTime? LastAttemptTime { get; set; }

    public DateTime? LeaseExpirationTime { get; set; }

    public DateTime? CompletedTime { get; set; }

    public string? LastFailureCode { get; set; }

    /// <summary>Sanitized diagnostic text; never contains exception messages or notification payload data.</summary>
    public string? LastFailureMessage { get; set; }

    public DateTime CreationTime { get; set; }
}

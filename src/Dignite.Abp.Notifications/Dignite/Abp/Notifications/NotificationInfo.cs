using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// An in-memory notification being published/distributed (before it becomes per-user store rows).
/// </summary>
public class NotificationInfo
{
    public Guid Id { get; set; }

    public string NotificationName { get; set; } = default!;

    public NotificationData? Data { get; set; }

    public string? EntityTypeName { get; set; }

    public string? EntityId { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    public DateTime CreationTime { get; set; }

    public Guid? TenantId { get; set; }
}

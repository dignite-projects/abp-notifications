using System;

namespace Dignite.Abp.Notifications;

/// <summary>Describes the enqueue outcome for one tenant-or-host scope.</summary>
public class NotificationAudienceBroadcastEnqueueResult
{
    /// <summary>The authoritative tenant id, or <see langword="null"/> for the host scope.</summary>
    public Guid? TenantId { get; }

    public Guid NotificationId { get; }

    public bool IsEnqueued { get; }

    public string? ErrorMessage { get; }

    public NotificationAudienceBroadcastEnqueueResult(
        Guid? tenantId,
        Guid notificationId,
        bool isEnqueued,
        string? errorMessage = null)
    {
        TenantId = tenantId;
        NotificationId = notificationId;
        IsEnqueued = isEnqueued;
        ErrorMessage = errorMessage;
    }
}

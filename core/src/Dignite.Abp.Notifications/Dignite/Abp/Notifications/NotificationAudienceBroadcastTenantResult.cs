using System;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceBroadcastTenantResult
{
    public Guid? TenantId { get; }

    public Guid NotificationId { get; }

    public bool IsEnqueued { get; }

    public string? ErrorMessage { get; }

    public NotificationAudienceBroadcastTenantResult(
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

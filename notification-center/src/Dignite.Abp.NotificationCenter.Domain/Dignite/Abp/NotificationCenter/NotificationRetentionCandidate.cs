using System;
using Dignite.Abp.Notifications;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Deletion candidate passed to retention archive/veto contributors before a physical delete.</summary>
public class NotificationRetentionCandidate
{
    public NotificationRetentionRecordKind RecordKind { get; }

    public Guid Id { get; }

    public Guid? TenantId { get; }

    public DateTime CreationTime { get; }

    public Guid? NotificationId { get; }

    public Guid? UserId { get; }

    public string? Channel { get; }

    public NotificationDeliveryState? DeliveryState { get; }

    public string Reason { get; }

    public NotificationRetentionCandidate(
        NotificationRetentionRecordKind recordKind,
        Guid id,
        Guid? tenantId,
        DateTime creationTime,
        string reason,
        Guid? notificationId = null,
        Guid? userId = null,
        string? channel = null,
        NotificationDeliveryState? deliveryState = null)
    {
        RecordKind = recordKind;
        Id = id;
        TenantId = tenantId;
        CreationTime = creationTime;
        Reason = reason;
        NotificationId = notificationId;
        UserId = userId;
        Channel = channel;
        DeliveryState = deliveryState;
    }
}

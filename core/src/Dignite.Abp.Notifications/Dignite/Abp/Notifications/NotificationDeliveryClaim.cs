using System;

namespace Dignite.Abp.Notifications;

public sealed class NotificationDeliveryClaim
{
    public Guid LeaseId { get; }

    public int AttemptCount { get; }

    public DateTime LeaseExpirationTime { get; }

    public NotificationDeliveryClaim(Guid leaseId, int attemptCount, DateTime leaseExpirationTime)
    {
        LeaseId = leaseId;
        AttemptCount = attemptCount;
        LeaseExpirationTime = leaseExpirationTime;
    }
}

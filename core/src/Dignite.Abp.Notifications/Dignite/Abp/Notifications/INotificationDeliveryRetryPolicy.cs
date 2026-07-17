using System;

namespace Dignite.Abp.Notifications;

public interface INotificationDeliveryRetryPolicy
{
    DateTime? GetNextAttemptTime(DateTime failedAt, int attemptCount);
}

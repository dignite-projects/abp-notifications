using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Resolves permanent opt-outs and quiet hours before work leaves the producing process. Implementations must return
/// exactly one decision for every supplied candidate and must keep the supplied tenant boundary.
/// </summary>
public interface INotificationDeliveryPreferenceEvaluator
{
    Task<IReadOnlyList<NotificationDeliveryPreferenceDecision>> EvaluateAsync(
        string notificationName,
        Guid? tenantId,
        IReadOnlyCollection<NotificationDeliveryPreferenceCandidate> candidates,
        NotificationDeliveryPreferenceBehavior behavior,
        CancellationToken cancellationToken = default);
}

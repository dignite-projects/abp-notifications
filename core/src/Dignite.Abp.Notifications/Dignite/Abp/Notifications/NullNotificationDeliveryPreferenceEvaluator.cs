using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>Deterministic Core-only default: every eligible external-channel candidate is delivered immediately.</summary>
[ExposeServices(typeof(INotificationDeliveryPreferenceEvaluator), typeof(NullNotificationDeliveryPreferenceEvaluator))]
public class NullNotificationDeliveryPreferenceEvaluator :
    INotificationDeliveryPreferenceEvaluator,
    ITransientDependency
{
    public Task<IReadOnlyList<NotificationDeliveryPreferenceDecision>> EvaluateAsync(
        string notificationName,
        Guid? tenantId,
        IReadOnlyCollection<NotificationDeliveryPreferenceCandidate> candidates,
        NotificationDeliveryPreferenceBehavior behavior,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<NotificationDeliveryPreferenceDecision> decisions = candidates
            .Select(candidate => NotificationDeliveryPreferenceDecision.Deliver(candidate.UserId, candidate.Channel))
            .ToList();
        return Task.FromResult(decisions);
    }
}

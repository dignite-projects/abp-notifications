using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Applies notification-definition eligibility policy to a batch of candidate recipients. Implementations may
/// replace the default per-user checks with a more efficient batched permission/feature evaluation. Implementations
/// must preserve the supplied tenant/host boundary and provide suitable diagnostics for excluded recipients; the
/// distributor audits trusted-system bypass use independently of this replaceable policy.
/// </summary>
public interface INotificationRecipientEligibilityEvaluator
{
    /// <param name="notificationName">The ordinal, case-sensitive notification definition name.</param>
    /// <param name="candidateUserIds">Distinct candidates after caller-supplied exclusions are removed.</param>
    /// <param name="tenantId">
    /// The notification's tenant. Evaluation must run in exactly this tenant or host context.
    /// </param>
    /// <param name="mode">Whether definition requirements are enforced or deliberately bypassed.</param>
    /// <param name="cancellationToken">Cancellation observed throughout recipient evaluation.</param>
    Task<NotificationRecipientEligibilityResult> EvaluateAsync(
        string notificationName,
        IReadOnlyCollection<Guid> candidateUserIds,
        Guid? tenantId,
        NotificationRecipientEligibilityMode mode,
        CancellationToken cancellationToken = default);
}

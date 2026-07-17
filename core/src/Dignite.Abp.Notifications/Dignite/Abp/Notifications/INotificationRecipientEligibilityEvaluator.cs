using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Applies notification-definition eligibility policy to a batch of candidate recipients. Implementations may
/// replace the default per-user checks with a more efficient batched permission/feature evaluation.
/// </summary>
public interface INotificationRecipientEligibilityEvaluator
{
    /// <param name="notificationName">The ordinal, case-sensitive notification definition name.</param>
    /// <param name="candidateUserIds">Distinct candidates after caller-supplied exclusions are removed.</param>
    /// <param name="tenantId">
    /// The notification's tenant. Evaluation must run in exactly this tenant or host context.
    /// </param>
    /// <param name="mode">Whether definition requirements are enforced or deliberately bypassed.</param>
    Task<NotificationRecipientEligibilityResult> EvaluateAsync(
        string notificationName,
        IReadOnlyCollection<Guid> candidateUserIds,
        Guid? tenantId,
        NotificationRecipientEligibilityMode mode);
}

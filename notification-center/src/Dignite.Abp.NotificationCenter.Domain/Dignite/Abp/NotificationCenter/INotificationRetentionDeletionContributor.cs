using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Allows applications to archive a retention candidate before deletion or veto deletion for audit/legal reasons.
/// Contributors run inside cleanup's bounded per-record flow; throwing records an error and leaves the row intact.
/// </summary>
public interface INotificationRetentionDeletionContributor
{
    Task<NotificationRetentionDeletionDecision> EvaluateAsync(
        NotificationRetentionCandidate candidate,
        CancellationToken cancellationToken = default);
}

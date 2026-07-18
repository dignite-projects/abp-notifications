using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.NotificationCenter;

public class TestNotificationRetentionDeletionContributor :
    INotificationRetentionDeletionContributor,
    ITransientDependency
{
    public static ConcurrentDictionary<Guid, string> VetoReasons { get; } = new();

    public static ConcurrentDictionary<Guid, string> ThrowReasons { get; } = new();

    public static ConcurrentDictionary<Guid, Func<NotificationRetentionCandidate, CancellationToken, Task>> Callbacks { get; } =
        new();

    public static void Reset()
    {
        VetoReasons.Clear();
        ThrowReasons.Clear();
        Callbacks.Clear();
    }

    public async Task<NotificationRetentionDeletionDecision> EvaluateAsync(
        NotificationRetentionCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        if (ThrowReasons.TryRemove(candidate.Id, out var exceptionMessage))
        {
            throw new InvalidOperationException(exceptionMessage);
        }

        if (Callbacks.TryRemove(candidate.Id, out var callback))
        {
            await callback(candidate, cancellationToken);
        }

        return VetoReasons.TryGetValue(candidate.Id, out var vetoReason)
            ? NotificationRetentionDeletionDecision.Veto(vetoReason)
            : NotificationRetentionDeletionDecision.Allow();
    }
}

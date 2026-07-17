using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryProcessor : ITransientDependency
{
    private const string FailureCode = "channel-execution-failed";

    protected INotificationDeliveryStore Store { get; }
    protected IReadOnlyList<INotificationNotifier> Notifiers { get; }
    protected INotificationDeliveryRetryPolicy RetryPolicy { get; }
    protected IClock Clock { get; }
    protected ICurrentTenant CurrentTenant { get; }
    protected NotificationOptions Options { get; }
    protected ILogger<NotificationDeliveryProcessor> Logger { get; }

    public NotificationDeliveryProcessor(
        INotificationDeliveryStore store,
        IEnumerable<INotificationDeliveryNotifier> reliableNotifiers,
        IEnumerable<INotificationNotifier<NotificationDeliveryEto>> legacyNotifiers,
        INotificationDeliveryRetryPolicy retryPolicy,
        IClock clock,
        ICurrentTenant currentTenant,
        IOptions<NotificationOptions> options,
        ILogger<NotificationDeliveryProcessor> logger)
    {
        Store = store;
        Notifiers = reliableNotifiers
            .Cast<INotificationNotifier>()
            .Concat(legacyNotifiers)
            // One implementation is commonly exposed through both the reliable and legacy contracts. Collapse
            // that duplicate registration without discarding a configurable implementation type used for two
            // different channel names.
            .GroupBy(notifier => (
                ImplementationType: notifier.GetType(),
                ChannelKey: NotificationDeliveryIdentity.NormalizeChannel(notifier.Name)))
            .Select(group => group.First())
            .ToList();
        RetryPolicy = retryPolicy;
        Clock = clock;
        CurrentTenant = currentTenant;
        Options = options.Value;
        Logger = logger;
    }

    public virtual async Task ProcessAsync(
        NotificationDeliveryWorkEto workItem,
        CancellationToken cancellationToken = default)
    {
        var notifier = ResolveNotifierOrNull(workItem.Channel);
        if (notifier == null)
        {
            // Distributed event subscribers receive every channel's work type. A process that does not host this
            // channel must leave the event and its authoritative consumer-owned state untouched.
            Logger.LogDebug(
                "Ignoring notification delivery {DeliveryId} because channel {Channel} is not hosted by this process.",
                workItem.DeliveryId,
                workItem.Channel);
            return;
        }

        using (CurrentTenant.Change(workItem.TenantId, null))
        {
            var claimedAt = Clock.Now;
            // Materialization and the first claim are one independently committed operation. In particular, an
            // event-inbox transaction must not create an uncommitted pending row that a nested claim cannot see.
            var claim = await Store.EnsureCreatedAndTryClaimAsync(
                workItem,
                claimedAt,
                Options.DeliveryLeaseDuration,
                Options.MaxDeliveryAttempts,
                cancellationToken);
            if (claim == null)
            {
                return;
            }

            NotificationDeliveryMetrics.ClaimCount.Add(1, CreateTags(workItem, claim.AttemptCount));

            try
            {
                NotificationDeliveryResult result;
                if (notifier is INotificationDeliveryNotifier reliableNotifier)
                {
                    result = await reliableNotifier.DeliverAsync(workItem)
                             ?? throw new InvalidOperationException("A notification notifier returned no result.");
                }
                else if (notifier is INotificationNotifier<NotificationDeliveryEto> legacyNotifier)
                {
                    await legacyNotifier.HandleEventAsync(workItem.ToLegacyEto());
                    result = NotificationDeliveryResult.Succeeded();
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Notifier '{notifier.GetType().FullName}' cannot execute notification delivery work items.");
                }

                var completedAt = Clock.Now;
                bool updated;
                string outcome;
                if (result.IsSuppressed)
                {
                    updated = await Store.MarkSuppressedAsync(
                        workItem.DeliveryId,
                        workItem.TenantId,
                        claim.LeaseId,
                        completedAt,
                        result.ReasonCode!,
                        cancellationToken);
                    outcome = "suppressed";
                }
                else
                {
                    updated = await Store.MarkSucceededAsync(
                        workItem.DeliveryId,
                        workItem.TenantId,
                        claim.LeaseId,
                        completedAt,
                        cancellationToken);
                    outcome = "succeeded";
                }

                if (!updated)
                {
                    Logger.LogWarning(
                        "Ignoring stale completion for notification delivery {DeliveryId}; its lease is no longer current.",
                        workItem.DeliveryId);
                    return;
                }

                NotificationDeliveryMetrics.OutcomeCount.Add(
                    1,
                    CreateTags(workItem, claim.AttemptCount, outcome));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failedAt = Clock.Now;
                var nextAttemptTime = RetryPolicy.GetNextAttemptTime(failedAt, claim.AttemptCount);
                var updated = await Store.MarkFailedAsync(
                    workItem.DeliveryId,
                    workItem.TenantId,
                    claim.LeaseId,
                    failedAt,
                    FailureCode,
                    nextAttemptTime,
                    cancellationToken);
                if (!updated)
                {
                    throw;
                }

                var outcome = nextAttemptTime.HasValue ? "failed" : "dead_letter";
                NotificationDeliveryMetrics.OutcomeCount.Add(
                    1,
                    CreateTags(workItem, claim.AttemptCount, outcome));

                // Deliberately exclude exception.Message, stack traces and NotificationData: provider exceptions may
                // contain addresses, tokens or payload fragments. Operators can correlate by delivery id.
                Logger.LogWarning(
                    "Notification delivery {DeliveryId} for channel {Channel} failed on attempt {AttemptCount} " +
                    "with exception type {ExceptionType}; outcome {Outcome}, next attempt {NextAttemptTime}.",
                    workItem.DeliveryId,
                    workItem.Channel,
                    claim.AttemptCount,
                    exception.GetType().FullName,
                    outcome,
                    nextAttemptTime);
            }
        }
    }

    protected virtual INotificationNotifier? ResolveNotifierOrNull(string channel)
    {
        var matches = Notifiers
            .Where(notifier => string.Equals(notifier.Name, channel, StringComparison.OrdinalIgnoreCase))
            .GroupBy(notifier => notifier.GetType())
            .Select(group => group.First())
            .ToList();
        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Multiple notification notifiers are registered for channel '{channel}'.");
        }

        return matches[0];
    }

    private static TagList CreateTags(
        NotificationDeliveryWorkEto workItem,
        int attemptCount,
        string? outcome = null)
    {
        var tags = new TagList
        {
            { "notification.name", workItem.NotificationName },
            { "delivery.channel", workItem.Channel },
            { "delivery.attempt", attemptCount }
        };
        if (outcome != null)
        {
            tags.Add("delivery.outcome", outcome);
        }

        return tags;
    }
}

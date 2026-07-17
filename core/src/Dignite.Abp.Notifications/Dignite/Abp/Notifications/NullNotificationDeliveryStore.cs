using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Default delivery store when NotificationCenter is absent. It keeps state only for this process lifetime so core
/// delivery, duplicate suppression and retries remain functional, but restarts lose progress and operator history.
/// </summary>
[ExposeServices(typeof(INotificationDeliveryStore), typeof(NullNotificationDeliveryStore))]
public class NullNotificationDeliveryStore :
    INotificationDeliveryStore,
    IBatchedNotificationDeliveryStore,
    ISingletonDependency
{
    private readonly object _sync = new object();
    private readonly Dictionary<Guid, VolatileDelivery> _deliveries = new Dictionary<Guid, VolatileDelivery>();

    public Task EnsureCreatedAsync(
        NotificationDeliveryWorkEto workItem,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateIdentity(workItem);

        lock (_sync)
        {
            if (_deliveries.TryGetValue(workItem.DeliveryId, out var existing))
            {
                if (!SameIdentity(existing.WorkItem, workItem))
                {
                    throw new InvalidOperationException("A notification delivery id was reused for another identity.");
                }

                return Task.CompletedTask;
            }

            _deliveries.Add(workItem.DeliveryId, new VolatileDelivery(Clone(workItem)));
        }

        return Task.CompletedTask;
    }

    public async Task EnsureCreatedAsync(
        IReadOnlyCollection<NotificationDeliveryWorkEto> workItems,
        CancellationToken cancellationToken = default)
    {
        foreach (var workItem in workItems)
        {
            await EnsureCreatedAsync(workItem, cancellationToken);
        }
    }

    public Task<NotificationDeliveryClaim?> TryClaimAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        TimeSpan leaseDuration,
        int maxAttempts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_deliveries.TryGetValue(deliveryId, out var delivery) || delivery.WorkItem.TenantId != tenantId)
            {
                return Task.FromResult<NotificationDeliveryClaim?>(null);
            }

            var isDue = delivery.State == NotificationDeliveryState.Pending
                        || delivery.State == NotificationDeliveryState.Failed
                        && (!delivery.NextAttemptTime.HasValue || delivery.NextAttemptTime <= now)
                        || delivery.State == NotificationDeliveryState.Claimed
                        && delivery.LeaseExpirationTime <= now;
            if (!isDue)
            {
                return Task.FromResult<NotificationDeliveryClaim?>(null);
            }

            if (delivery.AttemptCount >= maxAttempts)
            {
                delivery.State = NotificationDeliveryState.DeadLetter;
                delivery.LeaseId = null;
                delivery.LeaseExpirationTime = null;
                delivery.NextAttemptTime = null;
                delivery.DiagnosticCode = "attempts-exhausted";
                delivery.Diagnostic = "The delivery attempt limit was exhausted.";
                return Task.FromResult<NotificationDeliveryClaim?>(null);
            }

            delivery.State = NotificationDeliveryState.Claimed;
            delivery.AttemptCount++;
            delivery.LeaseId = Guid.NewGuid();
            delivery.LeaseExpirationTime = now.Add(leaseDuration);
            delivery.NextAttemptTime = null;
            return Task.FromResult<NotificationDeliveryClaim?>(new NotificationDeliveryClaim(
                delivery.LeaseId.Value,
                delivery.AttemptCount,
                delivery.LeaseExpirationTime.Value));
        }
    }

    public Task<bool> MarkSucceededAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        CancellationToken cancellationToken = default)
    {
        return CompleteAsync(
            deliveryId,
            tenantId,
            leaseId,
            NotificationDeliveryState.Succeeded,
            null,
            null,
            null,
            cancellationToken);
    }

    public Task<bool> MarkSuppressedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime completedAt,
        string reasonCode,
        CancellationToken cancellationToken = default)
    {
        return CompleteAsync(
            deliveryId,
            tenantId,
            leaseId,
            NotificationDeliveryState.Suppressed,
            reasonCode,
            "The channel intentionally suppressed this notification.",
            null,
            cancellationToken);
    }

    public Task<bool> MarkFailedAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        DateTime failedAt,
        string failureCode,
        DateTime? nextAttemptTime,
        CancellationToken cancellationToken = default)
    {
        return CompleteAsync(
            deliveryId,
            tenantId,
            leaseId,
            nextAttemptTime.HasValue ? NotificationDeliveryState.Failed : NotificationDeliveryState.DeadLetter,
            failureCode,
            "The channel failed to deliver this notification.",
            nextAttemptTime,
            cancellationToken);
    }

    public Task<IReadOnlyList<NotificationDeliveryWorkEto>> GetDueWorkItemsAsync(
        DateTime now,
        int maxResultCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var items = _deliveries.Values
                .Where(delivery => delivery.State == NotificationDeliveryState.Pending
                                   || delivery.State == NotificationDeliveryState.Failed
                                   && (!delivery.NextAttemptTime.HasValue || delivery.NextAttemptTime <= now)
                                   || delivery.State == NotificationDeliveryState.Claimed
                                   && delivery.LeaseExpirationTime <= now)
                .OrderBy(delivery => delivery.NextAttemptTime ?? delivery.LeaseExpirationTime ?? DateTime.MinValue)
                .Take(maxResultCount)
                .Select(delivery => Clone(delivery.WorkItem))
                .ToList();
            return Task.FromResult<IReadOnlyList<NotificationDeliveryWorkEto>>(items);
        }
    }

    public Task<bool> RequeueAsync(
        Guid deliveryId,
        Guid? tenantId,
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_deliveries.TryGetValue(deliveryId, out var delivery)
                || delivery.WorkItem.TenantId != tenantId
                || delivery.State != NotificationDeliveryState.Failed
                && delivery.State != NotificationDeliveryState.Suppressed
                && delivery.State != NotificationDeliveryState.DeadLetter)
            {
                return Task.FromResult(false);
            }

            delivery.State = NotificationDeliveryState.Pending;
            delivery.AttemptCount = 0;
            delivery.NextAttemptTime = now;
            delivery.LeaseId = null;
            delivery.LeaseExpirationTime = null;
            delivery.DiagnosticCode = null;
            delivery.Diagnostic = null;
            return Task.FromResult(true);
        }
    }

    private Task<bool> CompleteAsync(
        Guid deliveryId,
        Guid? tenantId,
        Guid leaseId,
        NotificationDeliveryState state,
        string? diagnosticCode,
        string? diagnostic,
        DateTime? nextAttemptTime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_deliveries.TryGetValue(deliveryId, out var delivery)
                || delivery.WorkItem.TenantId != tenantId
                || delivery.State != NotificationDeliveryState.Claimed
                || delivery.LeaseId != leaseId)
            {
                return Task.FromResult(false);
            }

            delivery.State = state;
            delivery.LeaseId = null;
            delivery.LeaseExpirationTime = null;
            delivery.NextAttemptTime = nextAttemptTime;
            delivery.DiagnosticCode = diagnosticCode;
            delivery.Diagnostic = diagnostic;
            return Task.FromResult(true);
        }
    }

    private static void ValidateIdentity(NotificationDeliveryWorkEto workItem)
    {
        var expectedId = NotificationDeliveryIdentity.CreateId(
            workItem.TenantId,
            workItem.NotificationId,
            workItem.UserId,
            workItem.Channel);
        var expectedKey = NotificationDeliveryIdentity.CreateIdempotencyKey(
            workItem.TenantId,
            workItem.NotificationId,
            workItem.UserId,
            workItem.Channel);
        if (workItem.DeliveryId != expectedId
            || !string.Equals(workItem.IdempotencyKey, expectedKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The notification delivery work-item identity is invalid.");
        }
    }

    private static bool SameIdentity(NotificationDeliveryWorkEto left, NotificationDeliveryWorkEto right)
    {
        return left.TenantId == right.TenantId
               && left.NotificationId == right.NotificationId
               && left.UserId == right.UserId
               && string.Equals(
                   NotificationDeliveryIdentity.NormalizeChannel(left.Channel),
                   NotificationDeliveryIdentity.NormalizeChannel(right.Channel),
                   StringComparison.Ordinal);
    }

    private static NotificationDeliveryWorkEto Clone(NotificationDeliveryWorkEto source)
    {
        return new NotificationDeliveryWorkEto
        {
            DeliveryId = source.DeliveryId,
            IdempotencyKey = source.IdempotencyKey,
            NotificationId = source.NotificationId,
            NotificationName = source.NotificationName,
            Data = source.Data,
            Severity = source.Severity,
            CreationTime = source.CreationTime,
            UserId = source.UserId,
            Channel = source.Channel,
            EntityTypeName = source.EntityTypeName,
            EntityId = source.EntityId,
            TenantId = source.TenantId
        };
    }

    private sealed class VolatileDelivery
    {
        public NotificationDeliveryWorkEto WorkItem { get; }
        public NotificationDeliveryState State { get; set; } = NotificationDeliveryState.Pending;
        public int AttemptCount { get; set; }
        public DateTime? NextAttemptTime { get; set; }
        public Guid? LeaseId { get; set; }
        public DateTime? LeaseExpirationTime { get; set; }
        public string? DiagnosticCode { get; set; }
        public string? Diagnostic { get; set; }

        public VolatileDelivery(NotificationDeliveryWorkEto workItem)
        {
            WorkItem = workItem;
        }
    }
}

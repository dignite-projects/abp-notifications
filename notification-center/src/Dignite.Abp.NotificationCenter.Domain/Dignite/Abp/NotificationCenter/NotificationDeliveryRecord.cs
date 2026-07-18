using System;
using Dignite.Abp.Notifications;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Durable state for one tenant/notification/user/channel delivery identity.</summary>
public class NotificationDeliveryRecord : BasicAggregateRoot<Guid>, IMultiTenant, IHasConcurrencyStamp
{
    /// <summary>Optimistic concurrency token used by both EF Core and MongoDB claim updates.</summary>
    public virtual string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Non-null tenant key used by provider-independent unique indexes; host is <see cref="Guid.Empty"/>.</summary>
    public virtual Guid TenantKey { get; protected set; }

    public virtual Guid NotificationId { get; protected set; }

    public virtual Guid UserId { get; protected set; }

    public virtual string Channel { get; protected set; } = default!;

    public virtual string ChannelKey { get; protected set; } = default!;

    public virtual NotificationDeliveryIntent Intent { get; protected set; }

    public virtual DateTime? DeliveryNotBefore { get; protected set; }

    public virtual string? PreferenceReasonCode { get; protected set; }

    public virtual string IdempotencyKey { get; protected set; } = default!;

    public virtual string NotificationName { get; protected set; } = default!;

    /// <summary>
    /// Stable System.Text.Json payload snapshot used by a separately deployed channel consumer to retry without
    /// requiring access to the producer's Notification row.
    /// </summary>
    public virtual string? Data { get; protected set; }

    public virtual string? EntityTypeName { get; protected set; }

    public virtual string? EntityId { get; protected set; }

    public virtual NotificationSeverity Severity { get; protected set; }

    public virtual NotificationDeliveryState State { get; protected set; }

    public virtual int AttemptCount { get; protected set; }

    public virtual DateTime? NextAttemptTime { get; protected set; }

    public virtual DateTime? LastAttemptTime { get; protected set; }

    public virtual Guid? LeaseId { get; protected set; }

    public virtual DateTime? LeaseExpirationTime { get; protected set; }

    public virtual DateTime? CompletedTime { get; protected set; }

    public virtual string? LastFailureCode { get; protected set; }

    public virtual string? LastFailureMessage { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    protected NotificationDeliveryRecord()
    {
    }

    public NotificationDeliveryRecord(
        Guid id,
        Guid notificationId,
        Guid userId,
        string channel,
        string idempotencyKey,
        string notificationName,
        string? data,
        string? entityTypeName,
        string? entityId,
        NotificationSeverity severity,
        DateTime creationTime,
        Guid? tenantId,
        NotificationDeliveryIntent intent = NotificationDeliveryIntent.Deliver,
        DateTime? deliveryNotBefore = null,
        string? preferenceReasonCode = null)
        : base(id)
    {
        TenantId = tenantId;
        TenantKey = tenantId ?? Guid.Empty;
        NotificationId = notificationId;
        UserId = userId;
        Channel = channel.Trim();
        ChannelKey = NotificationDeliveryIdentity.NormalizeChannel(channel);
        Intent = intent;
        DeliveryNotBefore = deliveryNotBefore;
        PreferenceReasonCode = preferenceReasonCode;
        IdempotencyKey = idempotencyKey;
        NotificationName = notificationName;
        Data = data;
        EntityTypeName = entityTypeName;
        EntityId = entityId;
        Severity = severity;
        CreationTime = creationTime;
        State = NotificationDeliveryState.Pending;
        NextAttemptTime = intent == NotificationDeliveryIntent.Delay ? deliveryNotBefore : null;
    }

    public virtual bool CanBeClaimed(DateTime now)
    {
        return State == NotificationDeliveryState.Pending
               && (!NextAttemptTime.HasValue || NextAttemptTime <= now)
               || State == NotificationDeliveryState.Failed
               && (!NextAttemptTime.HasValue || NextAttemptTime <= now)
               || State == NotificationDeliveryState.Claimed
               && LeaseExpirationTime <= now;
    }

    public virtual NotificationDeliveryClaim Claim(Guid leaseId, DateTime now, TimeSpan leaseDuration)
    {
        if (!CanBeClaimed(now))
        {
            throw new InvalidOperationException($"Delivery in state '{State}' is not claimable.");
        }

        State = NotificationDeliveryState.Claimed;
        AttemptCount++;
        LastAttemptTime = now;
        NextAttemptTime = null;
        CompletedTime = null;
        LeaseId = leaseId;
        LeaseExpirationTime = now.Add(leaseDuration);
        return new NotificationDeliveryClaim(leaseId, AttemptCount, LeaseExpirationTime.Value);
    }

    public virtual void MarkAbandonedDeadLetter(DateTime now)
    {
        if (!CanBeClaimed(now) || AttemptCount < 1)
        {
            throw new InvalidOperationException("Only due delivery work with previous attempts can exhaust its attempt limit.");
        }

        State = NotificationDeliveryState.DeadLetter;
        LeaseId = null;
        LeaseExpirationTime = null;
        NextAttemptTime = null;
        CompletedTime = now;
        LastFailureCode = "attempts-exhausted";
        LastFailureMessage = "The delivery attempt limit was exhausted.";
    }

    public virtual bool MarkSucceeded(Guid leaseId, DateTime completedAt)
    {
        if (!HasLease(leaseId))
        {
            return false;
        }

        Complete(NotificationDeliveryState.Succeeded, completedAt, null, null, null);
        return true;
    }

    public virtual bool MarkSuppressed(
        Guid leaseId,
        DateTime completedAt,
        string reasonCode)
    {
        if (!HasLease(leaseId))
        {
            return false;
        }

        Complete(
            NotificationDeliveryState.Suppressed,
            completedAt,
            reasonCode,
            "The channel intentionally suppressed this notification.",
            null);
        return true;
    }

    public virtual bool MarkFailed(
        Guid leaseId,
        DateTime failedAt,
        string failureCode,
        DateTime? nextAttemptTime)
    {
        if (!HasLease(leaseId))
        {
            return false;
        }

        Complete(
            nextAttemptTime.HasValue ? NotificationDeliveryState.Failed : NotificationDeliveryState.DeadLetter,
            failedAt,
            failureCode,
            "The channel failed to deliver this notification.",
            nextAttemptTime);
        return true;
    }

    public virtual bool Requeue(DateTime now)
    {
        if (State != NotificationDeliveryState.Failed
            && State != NotificationDeliveryState.Suppressed
            && State != NotificationDeliveryState.DeadLetter)
        {
            return false;
        }

        State = NotificationDeliveryState.Pending;
        AttemptCount = 0;
        NextAttemptTime = now;
        LastAttemptTime = null;
        LeaseId = null;
        LeaseExpirationTime = null;
        CompletedTime = null;
        LastFailureCode = null;
        LastFailureMessage = null;
        // A manual requeue is an explicit operator override: discard the producer's original suppress/delay
        // intent so the retried attempt is actually delivered instead of being immediately re-suppressed or
        // re-delayed by the processor reading a stale Intent.
        Intent = NotificationDeliveryIntent.Deliver;
        DeliveryNotBefore = null;
        PreferenceReasonCode = null;
        return true;
    }

    private bool HasLease(Guid leaseId)
    {
        return State == NotificationDeliveryState.Claimed && LeaseId == leaseId;
    }

    private void Complete(
        NotificationDeliveryState state,
        DateTime completedAt,
        string? failureCode,
        string? failureMessage,
        DateTime? nextAttemptTime)
    {
        State = state;
        LeaseId = null;
        LeaseExpirationTime = null;
        CompletedTime = completedAt;
        LastFailureCode = failureCode;
        LastFailureMessage = failureMessage;
        NextAttemptTime = nextAttemptTime;
    }
}

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

    /// <summary>The actor responsible for the most recent explicit force-delivery override.</summary>
    public virtual Guid? LastForceDeliveryActorId { get; protected set; }

    /// <summary>The time of the most recent explicit force-delivery override.</summary>
    public virtual DateTime? LastForceDeliveryTime { get; protected set; }

    /// <summary>The state immediately before the most recent explicit force-delivery override.</summary>
    public virtual NotificationDeliveryState? LastForceDeliveryPreviousState { get; protected set; }

    /// <summary>A stable, non-sensitive reason code for the most recent force-delivery override.</summary>
    public virtual string? LastForceDeliveryReasonCode { get; protected set; }

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
               || State == NotificationDeliveryState.RetryScheduled
               && (!NextAttemptTime.HasValue || NextAttemptTime <= now)
               || State == NotificationDeliveryState.Processing
               && LeaseExpirationTime <= now;
    }

    public virtual NotificationDeliveryClaim Claim(Guid leaseId, DateTime now, TimeSpan leaseDuration)
    {
        if (!CanBeClaimed(now))
        {
            throw new InvalidOperationException($"Delivery in state '{State}' is not claimable.");
        }

        State = NotificationDeliveryState.Processing;
        AttemptCount++;
        LastAttemptTime = now;
        NextAttemptTime = null;
        CompletedTime = null;
        LeaseId = leaseId;
        LeaseExpirationTime = now.Add(leaseDuration);
        return new NotificationDeliveryClaim(leaseId, AttemptCount, LeaseExpirationTime.Value);
    }

    public virtual void MarkAbandonedAsDeadLettered(DateTime now)
    {
        if (!CanBeClaimed(now) || AttemptCount < 1)
        {
            throw new InvalidOperationException("Only due delivery work with previous attempts can exhaust its attempt limit.");
        }

        State = NotificationDeliveryState.DeadLettered;
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
            nextAttemptTime.HasValue ? NotificationDeliveryState.RetryScheduled : NotificationDeliveryState.DeadLettered,
            failedAt,
            failureCode,
            "The channel failed to deliver this notification.",
            nextAttemptTime);
        return true;
    }

    public virtual bool Retry(DateTime now)
    {
        if (State != NotificationDeliveryState.RetryScheduled
            && State != NotificationDeliveryState.DeadLettered)
        {
            return false;
        }

        ResetForRetry(now);
        return true;
    }

    public virtual bool ForceDeliver(Guid actorId, DateTime now, string reasonCode)
    {
        if (actorId == Guid.Empty)
        {
            throw new ArgumentException("The force-delivery actor id cannot be empty.", nameof(actorId));
        }

        if (string.IsNullOrWhiteSpace(reasonCode)
            || reasonCode.Length > NotificationDeliveryOverrideReasonCodes.MaxLength)
        {
            throw new ArgumentException("The force-delivery reason code is invalid.", nameof(reasonCode));
        }

        if (State != NotificationDeliveryState.Suppressed)
        {
            return false;
        }

        LastForceDeliveryActorId = actorId;
        LastForceDeliveryTime = now;
        LastForceDeliveryPreviousState = State;
        LastForceDeliveryReasonCode = reasonCode;
        ResetForRetry(now);
        Intent = NotificationDeliveryIntent.Deliver;
        DeliveryNotBefore = null;
        PreferenceReasonCode = null;
        return true;
    }

    private void ResetForRetry(DateTime now)
    {
        State = NotificationDeliveryState.Pending;
        AttemptCount = 0;
        NextAttemptTime = now;
        LastAttemptTime = null;
        LeaseId = null;
        LeaseExpirationTime = null;
        CompletedTime = null;
        LastFailureCode = null;
        LastFailureMessage = null;
    }

    private bool HasLease(Guid leaseId)
    {
        return State == NotificationDeliveryState.Processing && LeaseId == leaseId;
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

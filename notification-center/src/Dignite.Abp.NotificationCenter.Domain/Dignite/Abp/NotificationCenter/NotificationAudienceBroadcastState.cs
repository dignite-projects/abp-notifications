using System;
using Dignite.Abp.Notifications;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Durable Notifications-owned workflow state for one tenant-or-host audience broadcast.</summary>
public class NotificationAudienceBroadcastState : BasicAggregateRoot<Guid>, IMultiTenant, IHasConcurrencyStamp
{
    public virtual string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TenantKey { get; protected set; }

    public virtual string NotificationName { get; protected set; } = default!;

    public virtual string AudienceName { get; protected set; } = default!;

    public virtual NotificationAudienceBroadcastStatus Status { get; protected set; }

    public virtual long CompletedPageCount { get; protected set; }

    public virtual long CandidateCount { get; protected set; }

    /// <summary>Opaque token identifying the next page of work.</summary>
    public virtual string? ContinuationToken { get; protected set; }

    public virtual bool IsCancellationRequested { get; protected set; }

    public virtual DateTime? CancellationRequestedTime { get; protected set; }

    public virtual string? FailureCode { get; protected set; }

    public virtual string? FailureMessage { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    public virtual DateTime LastUpdatedTime { get; protected set; }

    public virtual DateTime? CompletionTime { get; protected set; }

    protected NotificationAudienceBroadcastState()
    {
    }

    public NotificationAudienceBroadcastState(
        Guid notificationId,
        string notificationName,
        string audienceName,
        DateTime creationTime,
        Guid? tenantId)
        : base(notificationId)
    {
        if (notificationId == Guid.Empty)
        {
            throw new ArgumentException("Notification id cannot be Guid.Empty.", nameof(notificationId));
        }

        NotificationName = ValidateRequired(
            notificationName,
            nameof(notificationName),
            NotificationCenterConsts.MaxNotificationNameLength);
        AudienceName = ValidateRequired(
            audienceName,
            nameof(audienceName),
            NotificationCenterConsts.MaxAudienceNameLength);
        TenantId = tenantId;
        TenantKey = tenantId ?? Guid.Empty;
        Status = NotificationAudienceBroadcastStatus.Enqueued;
        CreationTime = creationTime;
        LastUpdatedTime = creationTime;
    }

    public virtual bool RecordStarted(string notificationName, string audienceName, DateTime updateTime)
    {
        if (IsTerminal(Status))
        {
            return false;
        }

        NotificationName = ValidateRequired(
            notificationName,
            nameof(notificationName),
            NotificationCenterConsts.MaxNotificationNameLength);
        AudienceName = ValidateRequired(
            audienceName,
            nameof(audienceName),
            NotificationCenterConsts.MaxAudienceNameLength);
        Status = IsCancellationRequested
            ? NotificationAudienceBroadcastStatus.CancellationRequested
            : Status == NotificationAudienceBroadcastStatus.Failed
                ? NotificationAudienceBroadcastStatus.Enqueued
                : Status;
        FailureCode = null;
        FailureMessage = null;
        CompletionTime = null;
        Touch(updateTime);
        return true;
    }

    public virtual bool RecordPageCompleted(
        long pageIndex,
        long candidateCount,
        string? nextContinuationToken,
        DateTime updateTime)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }

        if (candidateCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        }

        ValidateOptionalLength(
            nextContinuationToken,
            nameof(nextContinuationToken),
            NotificationCenterConsts.MaxBroadcastContinuationTokenLength);
        if (IsTerminal(Status) || pageIndex != CompletedPageCount)
        {
            return false;
        }

        CompletedPageCount++;
        CandidateCount += candidateCount;
        ContinuationToken = nextContinuationToken;
        Status = IsCancellationRequested
            ? NotificationAudienceBroadcastStatus.CancellationRequested
            : NotificationAudienceBroadcastStatus.Running;
        FailureCode = null;
        FailureMessage = null;
        Touch(updateTime);
        return true;
    }

    public virtual bool Complete(DateTime completionTime)
    {
        if (IsTerminal(Status))
        {
            return false;
        }

        Status = IsCancellationRequested
            ? NotificationAudienceBroadcastStatus.Canceled
            : NotificationAudienceBroadcastStatus.Completed;
        ContinuationToken = null;
        FailureCode = null;
        FailureMessage = null;
        CompletionTime = completionTime;
        Touch(completionTime);
        return true;
    }

    public virtual bool RequestCancellation(DateTime requestTime)
    {
        if (IsTerminal(Status))
        {
            return false;
        }

        IsCancellationRequested = true;
        CancellationRequestedTime ??= requestTime;
        Status = NotificationAudienceBroadcastStatus.CancellationRequested;
        Touch(requestTime);
        return true;
    }

    public virtual bool Cancel(DateTime completionTime)
    {
        if (IsTerminal(Status))
        {
            return false;
        }

        IsCancellationRequested = true;
        CancellationRequestedTime ??= completionTime;
        Status = NotificationAudienceBroadcastStatus.Canceled;
        ContinuationToken = null;
        CompletionTime = completionTime;
        Touch(completionTime);
        return true;
    }

    public virtual bool Fail(string failureCode, string failureMessage, DateTime updateTime)
    {
        if (IsTerminal(Status))
        {
            return false;
        }

        FailureCode = ValidateRequired(
            failureCode,
            nameof(failureCode),
            NotificationCenterConsts.MaxBroadcastFailureCodeLength);
        FailureMessage = ValidateRequired(
            failureMessage,
            nameof(failureMessage),
            NotificationCenterConsts.MaxBroadcastFailureMessageLength);
        Status = NotificationAudienceBroadcastStatus.Failed;
        CompletionTime = null;
        Touch(updateTime);
        return true;
    }

    public virtual NotificationAudienceBroadcastProgress ToProgress()
    {
        return new NotificationAudienceBroadcastProgress
        {
            TenantId = TenantId,
            NotificationId = Id,
            NotificationName = NotificationName,
            AudienceName = AudienceName,
            Status = Status,
            CompletedPageCount = CompletedPageCount,
            CandidateCount = CandidateCount,
            NextContinuationToken = ContinuationToken,
            IsCancellationRequested = IsCancellationRequested,
            CancellationRequestedTime = CancellationRequestedTime,
            FailureCode = FailureCode,
            FailureMessage = FailureMessage,
            CreationTime = CreationTime,
            LastUpdatedTime = LastUpdatedTime,
            CompletionTime = CompletionTime,
            ConcurrencyStamp = ConcurrencyStamp
        };
    }

    public static bool IsTerminal(NotificationAudienceBroadcastStatus status)
    {
        return status is NotificationAudienceBroadcastStatus.Completed
            or NotificationAudienceBroadcastStatus.Canceled;
    }

    private void Touch(DateTime updateTime)
    {
        LastUpdatedTime = updateTime;
    }

    private static string ValidateRequired(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        value = value.Trim();
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return value;
    }

    private static void ValidateOptionalLength(string? value, string parameterName, int maxLength)
    {
        if (value != null && (string.IsNullOrWhiteSpace(value) || value.Length > maxLength))
        {
            throw new ArgumentException(
                $"The value must be null or contain between 1 and {maxLength} characters.",
                parameterName);
        }
    }
}

using System;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceBroadcastProgress
{
    /// <summary>The authoritative tenant id, or <see langword="null"/> for the host scope.</summary>
    public Guid? TenantId { get; set; }

    public Guid NotificationId { get; set; }

    public string NotificationName { get; set; } = default!;

    public string AudienceName { get; set; } = default!;

    public NotificationAudienceBroadcastStatus Status { get; set; }

    public long CompletedPageCount { get; set; }

    public long CandidateCount { get; set; }

    /// <summary>Opaque token for the next page, or <see langword="null"/> when no next page is known.</summary>
    public string? NextContinuationToken { get; set; }

    /// <summary>Whether the opaque next continuation token identifies another page.</summary>
    public bool HasMore => !string.IsNullOrWhiteSpace(NextContinuationToken);

    public bool IsCancellationRequested { get; set; }

    public DateTime? CancellationRequestedTime { get; set; }

    public string? FailureCode { get; set; }

    public string? FailureMessage { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime LastUpdatedTime { get; set; }

    public DateTime? CompletionTime { get; set; }

    public string ConcurrencyStamp { get; set; } = default!;

    public NotificationAudienceBroadcastProgress Clone()
    {
        return new NotificationAudienceBroadcastProgress
        {
            TenantId = TenantId,
            NotificationId = NotificationId,
            NotificationName = NotificationName,
            AudienceName = AudienceName,
            Status = Status,
            CompletedPageCount = CompletedPageCount,
            CandidateCount = CandidateCount,
            NextContinuationToken = NextContinuationToken,
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
}

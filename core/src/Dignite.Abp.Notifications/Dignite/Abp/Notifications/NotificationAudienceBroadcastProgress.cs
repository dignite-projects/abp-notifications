using System;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceBroadcastProgress
{
    public Guid? TenantId { get; set; }

    public Guid NotificationId { get; set; }

    public string NotificationName { get; set; } = default!;

    public string AudienceName { get; set; } = default!;

    public NotificationAudienceBroadcastStatus Status { get; set; }

    public long CompletedPageCount { get; set; }

    public long CandidateCount { get; set; }

    public string? NextCursor { get; set; }

    public bool HasMore { get; set; }

    public bool IsCancellationRequested { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime LastUpdatedTime { get; set; }

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
            NextCursor = NextCursor,
            HasMore = HasMore,
            IsCancellationRequested = IsCancellationRequested,
            ErrorMessage = ErrorMessage,
            LastUpdatedTime = LastUpdatedTime
        };
    }
}

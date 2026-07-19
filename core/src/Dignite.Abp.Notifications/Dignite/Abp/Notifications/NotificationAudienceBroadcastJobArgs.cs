using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Resumable background-job arguments for one audience page.
/// </summary>
[Serializable]
public class NotificationAudienceBroadcastJobArgs
{
    /// <summary>
    /// The authoritative tenant id for the page, or <see langword="null"/> for host users.
    /// </summary>
    public Guid? TenantId { get; set; }

    public string AudienceName { get; set; } = default!;

    /// <summary>
    /// Prepared notification record shared by every page of the broadcast.
    /// </summary>
    public NotificationInfo Notification { get; set; } = default!;

    /// <summary>
    /// Opaque source continuation token for this page. <see langword="null"/> means the first page.
    /// </summary>
    public string? ContinuationToken { get; set; }

    /// <summary>
    /// Zero-based page index used for diagnostics.
    /// </summary>
    public long PageIndex { get; set; }

    public Guid[]? ExcludedUserIds { get; set; }

    public NotificationAudienceBroadcastJobArgs()
    {
    }

    public NotificationAudienceBroadcastJobArgs(
        Guid? tenantId,
        string audienceName,
        NotificationInfo notification,
        string? continuationToken,
        long pageIndex,
        Guid[]? excludedUserIds)
    {
        TenantId = tenantId;
        AudienceName = audienceName;
        Notification = notification;
        ContinuationToken = continuationToken;
        PageIndex = pageIndex;
        ExcludedUserIds = excludedUserIds;
    }

    public NotificationAudienceBroadcastJobArgs NextPage(string continuationToken)
    {
        return new NotificationAudienceBroadcastJobArgs(
            TenantId,
            AudienceName,
            Notification,
            continuationToken,
            PageIndex + 1,
            ExcludedUserIds);
    }
}

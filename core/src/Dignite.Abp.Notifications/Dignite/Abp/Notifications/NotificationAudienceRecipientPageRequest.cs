using System;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceRecipientPageRequest
{
    public string AudienceName { get; }

    /// <summary>The authoritative tenant id, or <see langword="null"/> for the host scope.</summary>
    public Guid? TenantId { get; }

    /// <summary>Opaque source continuation token, or <see langword="null"/> for the first page.</summary>
    public string? ContinuationToken { get; }

    public int MaxResultCount { get; }

    public NotificationAudienceRecipientPageRequest(
        string audienceName,
        Guid? tenantId,
        string? continuationToken,
        int maxResultCount)
    {
        if (string.IsNullOrWhiteSpace(audienceName))
        {
            throw new ArgumentException("Audience name is required.", nameof(audienceName));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResultCount);

        AudienceName = audienceName;
        TenantId = tenantId;
        ContinuationToken = continuationToken;
        MaxResultCount = maxResultCount;
    }
}

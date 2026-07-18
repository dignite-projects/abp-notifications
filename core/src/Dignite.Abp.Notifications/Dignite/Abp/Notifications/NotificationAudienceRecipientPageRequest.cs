using System;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceRecipientPageRequest
{
    public string AudienceName { get; }

    public Guid? TenantId { get; }

    public string? Cursor { get; }

    public int MaxResultCount { get; }

    public NotificationAudienceRecipientPageRequest(
        string audienceName,
        Guid? tenantId,
        string? cursor,
        int maxResultCount)
    {
        if (string.IsNullOrWhiteSpace(audienceName))
        {
            throw new ArgumentException("Audience name is required.", nameof(audienceName));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResultCount);

        AudienceName = audienceName;
        TenantId = tenantId;
        Cursor = cursor;
        MaxResultCount = maxResultCount;
    }
}

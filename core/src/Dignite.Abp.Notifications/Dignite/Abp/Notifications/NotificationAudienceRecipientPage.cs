using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceRecipientPage
{
    public IReadOnlyList<Guid> UserIds { get; }

    /// <summary>Opaque token for the next page, or <see langword="null"/> when this is the final page.</summary>
    public string? NextContinuationToken { get; }

    /// <summary>Whether the opaque next continuation token identifies another page.</summary>
    public bool HasMore => NextContinuationToken != null;

    public NotificationAudienceRecipientPage(
        IReadOnlyCollection<Guid> userIds,
        string? nextContinuationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (nextContinuationToken != null && string.IsNullOrWhiteSpace(nextContinuationToken))
        {
            throw new ArgumentException(
                "A next continuation token cannot be empty or whitespace.",
                nameof(nextContinuationToken));
        }

        UserIds = userIds.ToArray();
        NextContinuationToken = nextContinuationToken;
    }
}

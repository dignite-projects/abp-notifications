using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceRecipientPage
{
    public IReadOnlyList<Guid> UserIds { get; }

    public string? NextCursor { get; }

    public bool HasMore { get; }

    public NotificationAudienceRecipientPage(
        IReadOnlyCollection<Guid> userIds,
        string? nextCursor,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        UserIds = userIds.ToArray();
        NextCursor = nextCursor;
        HasMore = hasMore;
    }
}

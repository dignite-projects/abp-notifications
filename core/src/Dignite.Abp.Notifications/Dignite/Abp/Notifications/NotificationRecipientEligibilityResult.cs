using System;
using System.Collections.Generic;

namespace Dignite.Abp.Notifications;

/// <summary>The eligible and excluded partitions produced by one recipient-policy evaluation.</summary>
public class NotificationRecipientEligibilityResult
{
    public IReadOnlyList<Guid> EligibleUserIds { get; }

    public IReadOnlyList<Guid> ExcludedUserIds { get; }

    public NotificationRecipientEligibilityResult(
        IReadOnlyList<Guid> eligibleUserIds,
        IReadOnlyList<Guid> excludedUserIds)
    {
        EligibleUserIds = eligibleUserIds;
        ExcludedUserIds = excludedUserIds;
    }
}

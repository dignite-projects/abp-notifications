using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

/// <summary>Contains independent enqueue outcomes for an explicit set of tenant scopes.</summary>
public class NotificationAudienceMultiTenantBroadcastResult
{
    public IReadOnlyList<NotificationAudienceBroadcastEnqueueResult> Results { get; }

    public NotificationAudienceMultiTenantBroadcastResult(
        IReadOnlyCollection<NotificationAudienceBroadcastEnqueueResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = results.ToArray();
    }
}

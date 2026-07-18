using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

public class NotificationAudienceBroadcastResult
{
    public IReadOnlyList<NotificationAudienceBroadcastTenantResult> Tenants { get; }

    public NotificationAudienceBroadcastResult(
        IReadOnlyCollection<NotificationAudienceBroadcastTenantResult> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        Tenants = tenants.ToArray();
    }
}

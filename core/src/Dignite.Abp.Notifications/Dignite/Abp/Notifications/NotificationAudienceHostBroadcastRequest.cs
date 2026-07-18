using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Host-orchestrated broadcast request for an explicit set of tenant ids.
/// </summary>
public class NotificationAudienceHostBroadcastRequest
{
    /// <summary>
    /// Tenant ids to process independently. Host users are intentionally not included here.
    /// </summary>
    public IReadOnlyCollection<Guid> TenantIds { get; }

    /// <summary>
    /// Notification definition name to publish in each tenant.
    /// </summary>
    public string NotificationName { get; }

    /// <summary>
    /// Named recipient source to page in each tenant.
    /// </summary>
    public string AudienceName { get; set; } = NotificationAudienceNames.AllActiveUsers;

    public NotificationData? Data { get; set; }

    public NotificationEntityIdentifier? EntityIdentifier { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>
    /// Optional user ids removed in every tenant before the prepared distribution pipeline evaluates eligibility.
    /// </summary>
    public Guid[]? ExcludedUserIds { get; set; }

    public NotificationAudienceHostBroadcastRequest(
        IReadOnlyCollection<Guid> tenantIds,
        string notificationName)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        if (tenantIds.Count == 0)
        {
            throw new ArgumentException("At least one tenant identifier is required.", nameof(tenantIds));
        }

        if (tenantIds.Any(tenantId => tenantId == Guid.Empty))
        {
            throw new ArgumentException("Tenant identifiers cannot be Guid.Empty.", nameof(tenantIds));
        }

        if (string.IsNullOrWhiteSpace(notificationName))
        {
            throw new ArgumentException("Notification name is required.", nameof(notificationName));
        }

        TenantIds = tenantIds.Distinct().ToArray();
        NotificationName = notificationName;
    }
}

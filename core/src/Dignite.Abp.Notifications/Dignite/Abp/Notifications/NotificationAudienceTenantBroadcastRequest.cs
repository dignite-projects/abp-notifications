using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Starts a large-audience broadcast for one explicit tenant or host scope.
/// </summary>
public class NotificationAudienceTenantBroadcastRequest
{
    /// <summary>
    /// The authoritative tenant id for the broadcast, or <see langword="null"/> for host users.
    /// </summary>
    public Guid? TenantId { get; }

    /// <summary>
    /// Notification definition name to publish.
    /// </summary>
    public string NotificationName { get; }

    /// <summary>
    /// Named recipient source to page.
    /// </summary>
    public string AudienceName { get; set; } = NotificationAudienceNames.AllActiveUsers;

    public NotificationData? Data { get; set; }

    public NotificationEntityIdentifier? EntityIdentifier { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>
    /// Optional user ids removed before the prepared distribution pipeline evaluates eligibility.
    /// </summary>
    public Guid[]? ExcludedUserIds { get; set; }

    public NotificationAudienceTenantBroadcastRequest(Guid? tenantId, string notificationName)
    {
        if (string.IsNullOrWhiteSpace(notificationName))
        {
            throw new ArgumentException("Notification name is required.", nameof(notificationName));
        }

        TenantId = tenantId;
        NotificationName = notificationName;
    }
}

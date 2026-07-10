using System;
using Volo.Abp;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Context used by email address resolvers. Tenant information is explicit so local repository-backed
/// resolvers and remote/microservice-backed resolvers can handle it in their own boundary.
/// </summary>
public class EmailNotificationAddressResolveContext
{
    public NotificationDelivery Notification { get; }

    public Guid UserId { get; }

    public Guid? TenantId { get; }

    public EmailNotificationAddressResolveContext(
        NotificationDelivery notification,
        Guid userId,
        Guid? tenantId)
    {
        Notification = Check.NotNull(notification, nameof(notification));
        UserId = userId;
        TenantId = tenantId;
    }
}


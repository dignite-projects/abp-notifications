using System;
using Volo.Abp;

namespace Dignite.Abp.Notifications.Emailing;

public class NotificationEmailBuildContext
{
    public NotificationDelivery Notification { get; }

    public Guid UserId { get; }

    public string EmailAddress { get; }

    public Guid? TenantId { get; }

    public NotificationEmailBuildContext(
        NotificationDelivery notification,
        Guid userId,
        string emailAddress,
        Guid? tenantId)
    {
        Notification = Check.NotNull(notification, nameof(notification));
        UserId = userId;
        EmailAddress = Check.NotNullOrWhiteSpace(emailAddress, nameof(emailAddress));
        TenantId = tenantId;
    }
}

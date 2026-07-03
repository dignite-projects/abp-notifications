using System;

namespace Dignite.Abp.Notifications;

public class NotificationSubscriptionInfo
{
    public Guid UserId { get; set; }

    public string NotificationName { get; set; } = default!;

    public string? EntityTypeName { get; set; }

    public string? EntityId { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid? TenantId { get; set; }
}

using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>A user's subscription to a notification type, optionally scoped to a specific entity.</summary>
public class NotificationSubscription : BasicAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid UserId { get; protected set; }

    public virtual string NotificationName { get; protected set; } = default!;

    public virtual string? EntityTypeName { get; protected set; }

    public virtual string? EntityId { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    protected NotificationSubscription()
    {
    }

    public NotificationSubscription(
        Guid id,
        Guid userId,
        string notificationName,
        string? entityTypeName,
        string? entityId,
        DateTime creationTime,
        Guid? tenantId)
        : base(id)
    {
        UserId = userId;
        NotificationName = notificationName;
        EntityTypeName = entityTypeName;
        EntityId = entityId;
        CreationTime = creationTime;
        TenantId = tenantId;
    }
}

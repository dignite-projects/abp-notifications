using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>A permanent allow/deny rule for one user and optional notification/channel scope.</summary>
public class NotificationDeliveryPreference : BasicAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TenantKey { get; protected set; }

    public virtual Guid UserId { get; protected set; }

    public virtual string? NotificationName { get; protected set; }

    public virtual string NotificationNameKey { get; protected set; } = default!;

    public virtual string? Channel { get; protected set; }

    public virtual string ChannelKey { get; protected set; } = default!;

    /// <summary>Whether a delivery matching this rule is allowed.</summary>
    public virtual bool IsDeliveryEnabled { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    public virtual DateTime LastModificationTime { get; protected set; }

    protected NotificationDeliveryPreference()
    {
    }

    public NotificationDeliveryPreference(
        Guid id,
        Guid userId,
        string? notificationName,
        string? channel,
        bool isDeliveryEnabled,
        DateTime creationTime,
        Guid? tenantId)
        : base(id)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A preference user identifier cannot be Guid.Empty.", nameof(userId));
        }

        TenantId = tenantId;
        TenantKey = NotificationDeliveryPreferenceIdentity.GetTenantKey(tenantId);
        UserId = userId;
        NotificationNameKey = NotificationDeliveryPreferenceIdentity.GetNotificationNameKey(notificationName);
        NotificationName = notificationName?.Trim();
        ChannelKey = NotificationDeliveryPreferenceIdentity.GetChannelKey(channel);
        Channel = channel?.Trim();
        IsDeliveryEnabled = isDeliveryEnabled;
        CreationTime = creationTime;
        LastModificationTime = creationTime;
    }

    public virtual void SetDeliveryEnabled(bool isDeliveryEnabled, DateTime modificationTime)
    {
        IsDeliveryEnabled = isDeliveryEnabled;
        LastModificationTime = modificationTime;
    }
}

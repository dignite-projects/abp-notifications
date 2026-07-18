using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>A user's daily local quiet-hours window. Start is inclusive and end is exclusive.</summary>
public class NotificationQuietHours : BasicAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TenantKey { get; protected set; }

    public virtual Guid UserId { get; protected set; }

    public virtual int StartMinute { get; protected set; }

    public virtual int EndMinute { get; protected set; }

    public virtual string TimeZoneId { get; protected set; } = default!;

    public virtual DateTime CreationTime { get; protected set; }

    public virtual DateTime LastModificationTime { get; protected set; }

    protected NotificationQuietHours()
    {
    }

    public NotificationQuietHours(
        Guid id,
        Guid userId,
        int startMinute,
        int endMinute,
        string timeZoneId,
        DateTime creationTime,
        Guid? tenantId)
        : base(id)
    {
        TenantId = tenantId;
        TenantKey = NotificationDeliveryPreferenceIdentity.GetTenantKey(tenantId);
        UserId = userId;
        CreationTime = creationTime;
        Set(startMinute, endMinute, timeZoneId, creationTime);
    }

    public virtual void Set(int startMinute, int endMinute, string timeZoneId, DateTime modificationTime)
    {
        if (UserId == Guid.Empty)
        {
            throw new ArgumentException("A quiet-hours user identifier cannot be Guid.Empty.", nameof(UserId));
        }

        if (startMinute < 0 || startMinute >= 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(startMinute));
        }

        if (endMinute < 0 || endMinute >= 24 * 60 || endMinute == startMinute)
        {
            throw new ArgumentOutOfRangeException(nameof(endMinute), "EndMinute must be a different minute of day.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId)
            || timeZoneId.Length > NotificationCenterConsts.MaxTimeZoneIdLength)
        {
            throw new ArgumentException("A valid time-zone identifier is required.", nameof(timeZoneId));
        }

        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        StartMinute = startMinute;
        EndMinute = endMinute;
        TimeZoneId = timeZoneId.Trim();
        LastModificationTime = modificationTime;
    }
}

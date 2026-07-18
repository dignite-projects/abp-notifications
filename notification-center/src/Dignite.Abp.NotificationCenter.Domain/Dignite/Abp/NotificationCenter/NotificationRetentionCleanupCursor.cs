using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Persistent scan cursor for bounded retention cleanup passes.</summary>
public class NotificationRetentionCleanupCursor : BasicAggregateRoot<Guid>, IMultiTenant, IHasConcurrencyStamp
{
    public virtual string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TenantKey { get; protected set; }

    public virtual bool IsTenantScoped { get; protected set; }

    public virtual NotificationRetentionRecordKind RecordKind { get; protected set; }

    public virtual DateTime? LastCreationTime { get; protected set; }

    public virtual Guid? LastRecordId { get; protected set; }

    public virtual DateTime CreationTime { get; protected set; }

    public virtual DateTime LastModificationTime { get; protected set; }

    protected NotificationRetentionCleanupCursor()
    {
    }

    public NotificationRetentionCleanupCursor(
        Guid id,
        NotificationRetentionRecordKind recordKind,
        bool isTenantScoped,
        Guid? tenantId,
        DateTime creationTime)
        : base(id)
    {
        if (!Enum.IsDefined(recordKind))
        {
            throw new ArgumentOutOfRangeException(nameof(recordKind), recordKind, null);
        }

        IsTenantScoped = isTenantScoped;
        TenantId = isTenantScoped ? tenantId : null;
        TenantKey = isTenantScoped ? tenantId ?? Guid.Empty : Guid.Empty;
        RecordKind = recordKind;
        CreationTime = creationTime;
        LastModificationTime = creationTime;
    }

    public virtual void MoveTo(DateTime creationTime, Guid recordId, DateTime modificationTime)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("A cleanup cursor record identifier cannot be Guid.Empty.", nameof(recordId));
        }

        LastCreationTime = creationTime;
        LastRecordId = recordId;
        LastModificationTime = modificationTime;
    }

    public virtual void Reset(DateTime modificationTime)
    {
        LastCreationTime = null;
        LastRecordId = null;
        LastModificationTime = modificationTime;
    }
}

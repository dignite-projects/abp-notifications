using System;

namespace Dignite.Abp.NotificationCenter;

public class NotificationRetentionCleanupRequest
{
    /// <summary>Reports eligible deletions without physically deleting records.</summary>
    public bool IsDryRun { get; set; }

    /// <summary>Overrides the cleanup clock, primarily for deterministic tests and scheduled dry-run reports.</summary>
    public DateTime? Now { get; set; }

    /// <summary>Overrides <see cref="NotificationRetentionOptions.CleanupBatchSize"/> for this pass.</summary>
    public int? BatchSize { get; set; }

    /// <summary>
    /// When true, cleanup is limited to <see cref="TenantId"/>. A scoped request with a null tenant targets host
    /// records. The default false value scans all tenants with explicit tenant predicates on every delete.
    /// </summary>
    public bool IsTenantScoped { get; set; }

    public Guid? TenantId { get; set; }
}

using System;
using Shouldly;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Confirms the retention worker resolves correctly from each provider's real DI container and runs on the
/// ABP periodic lifecycle. Lock-contention, cancellation, and validation mechanics are provider-independent
/// (they run against a bare service collection, not this host) and live once in
/// <c>NotificationRetentionCleanupWorker_Tests</c> in the EF Core test project instead of here.
/// </summary>
public abstract class NotificationPeriodicWorker_Tests<TStartupModule> :
    NotificationCenterTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public void Retention_worker_uses_the_ABP_periodic_lifecycle()
    {
        var worker = GetRequiredService<NotificationRetentionCleanupWorker>();

        worker.ShouldBeAssignableTo<IBackgroundWorker>();
        worker.Period.ShouldBe((int)TimeSpan.FromHours(1).TotalMilliseconds);
    }
}

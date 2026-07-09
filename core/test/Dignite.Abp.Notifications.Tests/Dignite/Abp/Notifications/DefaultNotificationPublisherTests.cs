using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationPublisherTests
{
    private readonly INotificationDistributor _distributor = Substitute.For<INotificationDistributor>();
    private readonly IBackgroundJobManager _backgroundJobManager = Substitute.For<IBackgroundJobManager>();

    private DefaultNotificationPublisher CreatePublisher(int threshold, Guid? tenantId = null)
    {
        var options = Options.Create(new NotificationOptions
        {
            DirectDistributionUserThreshold = threshold
        });

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);

        var currentTenant = Substitute.For<ICurrentTenant>();
        currentTenant.Id.Returns(tenantId);

        return new DefaultNotificationPublisher(
            options,
            _distributor,
            _backgroundJobManager,
            guidGenerator,
            clock,
            currentTenant);
    }

    [Fact]
    public async Task Distributes_inline_when_at_or_below_threshold()
    {
        var tenantId = Guid.NewGuid();
        var publisher = CreatePublisher(threshold: 3, tenantId);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _distributor.Received(1).DistributeAsync(
            Arg.Is<NotificationInfo>(n => n.TenantId == tenantId),
            Arg.Is<Guid[]>(u => u.Length == 3),
            Arg.Any<Guid[]?>());
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Enqueues_background_job_when_above_threshold()
    {
        var tenantId = Guid.NewGuid();
        var publisher = CreatePublisher(threshold: 2, tenantId);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _backgroundJobManager.Received(1).EnqueueAsync(
            Arg.Is<NotificationDistributionJobArgs>(args => args.Notification.TenantId == tenantId));
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }

    [Fact]
    public async Task Distribution_job_runs_inside_the_notification_tenant_scope()
    {
        var tenantId = Guid.NewGuid();
        var distributor = Substitute.For<INotificationDistributor>();
        var currentTenant = Substitute.For<ICurrentTenant>();
        var tenantScope = Substitute.For<IDisposable>();
        currentTenant.Change(tenantId, null).Returns(tenantScope);

        var job = new NotificationDistributionJob(distributor, currentTenant);
        var args = new NotificationDistributionJobArgs(
            new NotificationInfo
            {
                Id = Guid.NewGuid(),
                NotificationName = "test",
                TenantId = tenantId
            },
            new[] { Guid.NewGuid() },
            null);

        await job.ExecuteAsync(args);

        currentTenant.Received(1).Change(tenantId, null);
        await distributor.Received(1).DistributeAsync(args.Notification, args.UserIds, args.ExcludedUserIds);
        tenantScope.Received(1).Dispose();
    }
}

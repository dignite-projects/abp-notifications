using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using NSubstitute;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationPublisherTests
{
    private readonly INotificationDistributor _distributor = Substitute.For<INotificationDistributor>();
    private readonly IBackgroundJobManager _backgroundJobManager = Substitute.For<IBackgroundJobManager>();

    private DefaultNotificationPublisher CreatePublisher(int threshold)
    {
        var options = Options.Create(new NotificationOptions
        {
            DirectDistributionUserThreshold = threshold
        });

        var guidGenerator = Substitute.For<IGuidGenerator>();
        guidGenerator.Create().Returns(_ => Guid.NewGuid());

        var clock = Substitute.For<IClock>();
        clock.Now.Returns(DateTime.UtcNow);

        return new DefaultNotificationPublisher(options, _distributor, _backgroundJobManager, guidGenerator, clock);
    }

    [Fact]
    public async Task Distributes_inline_when_at_or_below_threshold()
    {
        var publisher = CreatePublisher(threshold: 3);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _distributor.Received(1).DistributeAsync(
            Arg.Any<NotificationInfo>(),
            Arg.Is<Guid[]>(u => u.Length == 3),
            Arg.Any<Guid[]?>());
        await _backgroundJobManager.DidNotReceiveWithAnyArgs()
            .EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
    }

    [Fact]
    public async Task Enqueues_background_job_when_above_threshold()
    {
        var publisher = CreatePublisher(threshold: 2);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        await publisher.PublishAsync("test", userIds: users);

        await _backgroundJobManager.Received(1).EnqueueAsync(Arg.Any<NotificationDistributionJobArgs>());
        await _distributor.DidNotReceiveWithAnyArgs().DistributeAsync(default!, default, default);
    }
}

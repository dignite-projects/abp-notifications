using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MongoDB.DistributedEvents;

namespace Dignite.Abp.NotificationCenter.MongoDB;

internal sealed class NotificationCenterMongoDbOutboxCapabilityHostedService : IHostedService
{
    private readonly AbpDistributedEventBusOptions _eventBusOptions;
    private readonly INotificationCenterMongoDbOutboxCapabilityChecker _capabilityChecker;
    private readonly ILogger<NotificationCenterMongoDbOutboxCapabilityHostedService> _logger;

    public NotificationCenterMongoDbOutboxCapabilityHostedService(
        IOptions<AbpDistributedEventBusOptions> eventBusOptions,
        INotificationCenterMongoDbOutboxCapabilityChecker capabilityChecker,
        ILogger<NotificationCenterMongoDbOutboxCapabilityHostedService> logger)
    {
        _eventBusOptions = eventBusOptions.Value;
        _capabilityChecker = capabilityChecker;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var outboxType = typeof(IMongoDbContextEventOutbox<NotificationCenterMongoDbContext>);
        var inboxType = typeof(IMongoDbContextEventInbox<NotificationCenterMongoDbContext>);
        var outboxConfigured = _eventBusOptions.Outboxes.Values
            .Any(config => config.ImplementationType == outboxType);
        var inboxConfigured = _eventBusOptions.Inboxes.Values
            .Any(config => config.ImplementationType == inboxType);

        if (!outboxConfigured && !inboxConfigured)
        {
            return;
        }

        if (!outboxConfigured || !inboxConfigured)
        {
            throw new AbpInitializationException(
                "The Notification Center MongoDB event boxes are only partially configured. " +
                $"Call {nameof(NotificationCenterDistributedEventBusOptionsExtensions.UseNotificationCenterMongoDbOutbox)} " +
                "to configure both the outbox and inbox.");
        }

        NotificationCenterMongoDbOutboxCapability capability;
        try
        {
            capability = await _capabilityChecker.CheckAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AbpInitializationException(
                "Could not verify that the Notification Center MongoDB deployment supports transactions. " +
                "The outbox/inbox atomicity guarantee has not been enabled.",
                exception);
        }

        if (!capability.IsSupported)
        {
            throw new AbpInitializationException(
                "Notification Center MongoDB outbox/inbox requires a verified transaction-capable replica set " +
                $"running MongoDB 4.0 or later. {capability.Diagnostic}");
        }

        _logger.LogInformation(
            "Notification Center MongoDB outbox/inbox transaction capability verified by a committed " +
            "multi-collection probe: {Topology}, maxWireVersion {MaxWireVersion}.",
            capability.Topology,
            capability.MaxWireVersion);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

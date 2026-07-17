using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DistributedEvents;

namespace Dignite.Abp.NotificationCenter.MongoDB;

[DependsOn(
    typeof(AbpNotificationCenterDomainModule),
    typeof(AbpMongoDbModule)
    )]
public class AbpNotificationCenterMongoDbModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMongoDbContext<NotificationCenterMongoDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });
    }

    public override async Task OnPreApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var eventBusOptions = context.ServiceProvider
            .GetRequiredService<IOptions<AbpDistributedEventBusOptions>>()
            .Value;

        var outboxType = typeof(IMongoDbContextEventOutbox<NotificationCenterMongoDbContext>);
        var inboxType = typeof(IMongoDbContextEventInbox<NotificationCenterMongoDbContext>);
        var outboxConfigured = eventBusOptions.Outboxes.Values.Any(config => config.ImplementationType == outboxType);
        var inboxConfigured = eventBusOptions.Inboxes.Values.Any(config => config.ImplementationType == inboxType);

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
            capability = await context.ServiceProvider
                .GetRequiredService<INotificationCenterMongoDbOutboxCapabilityChecker>()
                .CheckAsync();
        }
        catch (Exception exception)
        {
            throw new AbpInitializationException(
                "Could not verify that the Notification Center MongoDB deployment supports transactions. " +
                "The outbox/inbox atomicity guarantee has not been enabled.",
                exception);
        }

        if (!capability.IsSupported)
        {
            throw new AbpInitializationException(
                "Notification Center MongoDB outbox/inbox requires a transaction-capable replica set " +
                $"(MongoDB 4.0+) or sharded cluster (MongoDB 4.2+). {capability.Diagnostic}");
        }

        context.ServiceProvider
            .GetRequiredService<ILogger<AbpNotificationCenterMongoDbModule>>()
            .LogInformation(
                "Notification Center MongoDB outbox/inbox transaction capability verified: {Topology}, maxWireVersion {MaxWireVersion}.",
                capability.Topology,
                capability.MaxWireVersion);
    }
}

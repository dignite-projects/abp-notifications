using System;
using System.Threading.Tasks;
using Dignite.Abp.NotificationCenter.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoSandbox;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EventBus.Local;
using Volo.Abp.Guids;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DistributedEvents;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Tracing;
using Volo.Abp.Uow;
using Xunit;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the transactional event-box contract and MongoDB capability scenarios on real topologies.</summary>
[Collection(MongoTestCollection.Name)]
public class Notification_Outbox_Tests :
    Notification_Outbox_Tests<AbpNotificationCenterMongoDbOutboxTestModule>
{
    protected override async Task<long> GetOutboxCountAsync()
    {
        var dbContext = await GetRequiredService<IMongoDbContextProvider<NotificationCenterMongoDbContext>>()
            .GetDbContextAsync();

        if (dbContext.SessionHandle != null)
        {
            return await dbContext.OutgoingEvents.CountDocumentsAsync(
                dbContext.SessionHandle,
                Builders<OutgoingEventRecord>.Filter.Empty);
        }

        return await dbContext.OutgoingEvents.CountDocumentsAsync(Builders<OutgoingEventRecord>.Filter.Empty);
    }

    protected override async Task AssertProviderTransactionActiveAsync()
    {
        var dbContext = await GetRequiredService<IMongoDbContextProvider<NotificationCenterMongoDbContext>>()
            .GetDbContextAsync();
        dbContext.SessionHandle.ShouldNotBeNull();
        dbContext.SessionHandle!.IsInTransaction.ShouldBeTrue();
    }

    [Fact]
    public void Opt_in_configures_both_ABP_event_boxes_on_the_notification_context()
    {
        var options = GetRequiredService<IOptions<AbpDistributedEventBusOptions>>().Value;

        options.Outboxes.Values.ShouldHaveSingleItem().ImplementationType.ShouldBe(
            typeof(IMongoDbContextEventOutbox<NotificationCenterMongoDbContext>));
        options.Inboxes.Values.ShouldHaveSingleItem().ImplementationType.ShouldBe(
            typeof(IMongoDbContextEventInbox<NotificationCenterMongoDbContext>));
    }

    [Fact]
    public async Task Capability_checker_reports_the_replica_set_transaction_guarantee()
    {
        var capability = await GetRequiredService<INotificationCenterMongoDbOutboxCapabilityChecker>()
            .CheckAsync();

        capability.IsSupported.ShouldBeTrue();
        capability.Topology.ShouldBe(NotificationCenterMongoDbTopology.ReplicaSet);
        capability.SupportsLogicalSessions.ShouldBeTrue();
        capability.MaxWireVersion.ShouldBeGreaterThanOrEqualTo(7);
    }

    [Fact]
    public async Task Event_box_collections_use_ABP_names_and_expected_query_indexes()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await GetRequiredService<IMongoDbContextProvider<NotificationCenterMongoDbContext>>()
                .GetDbContextAsync();

            dbContext.OutgoingEvents.CollectionNamespace.CollectionName.ShouldBe("AbpEventOutbox");
            dbContext.IncomingEvents.CollectionNamespace.CollectionName.ShouldBe("AbpEventInbox");

            var outgoingIndexes = await (await dbContext.OutgoingEvents.Indexes.ListAsync()).ToListAsync();
            outgoingIndexes.ShouldContain(index =>
                index["key"].AsBsonDocument == new BsonDocument(nameof(OutgoingEventRecord.CreationTime), 1));

            var incomingIndexes = await (await dbContext.IncomingEvents.Indexes.ListAsync()).ToListAsync();
            incomingIndexes.ShouldContain(index =>
                index["key"].AsBsonDocument == new BsonDocument
                {
                    { nameof(IncomingEventRecord.Status), 1 },
                    { nameof(IncomingEventRecord.CreationTime), 1 }
                });
            incomingIndexes.ShouldContain(index =>
                index["key"].AsBsonDocument == new BsonDocument(nameof(IncomingEventRecord.MessageId), 1));
        });
    }

    [Fact]
    public async Task ABP_inbox_semantics_ignore_a_redelivery_with_the_same_message_id()
    {
        var messageId = Guid.NewGuid().ToString("N");
        var eventBus = ActivatorUtilities.CreateInstance<TestLocalDistributedEventBus>(ServiceProvider);

        await WithUnitOfWorkAsync(
            () => eventBus.AddToInboxForTestAsync(messageId),
            isTransactional: true);
        await WithUnitOfWorkAsync(
            () => eventBus.AddToInboxForTestAsync(messageId),
            isTransactional: true);

        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await GetRequiredService<IMongoDbContextProvider<NotificationCenterMongoDbContext>>()
                .GetDbContextAsync();
            var count = await dbContext.IncomingEvents.AsQueryable()
                .CountAsync(record => record.MessageId == messageId);
            count.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Startup_rejects_a_standalone_server_instead_of_claiming_atomicity()
    {
        using var runner = MongoRunner.Run(new MongoRunnerOptions
        {
            UseSingleNodeReplicaSet = false
        });
        AbpNotificationCenterStandaloneMongoDbOutboxTestModule.ConnectionString =
            MongoDbFixture.GetConnectionString(runner, "Standalone_" + Guid.NewGuid().ToString("N"));

        using var application = await AbpApplicationFactory
            .CreateAsync<AbpNotificationCenterStandaloneMongoDbOutboxTestModule>();

        var exception = await Should.ThrowAsync<AbpInitializationException>(
            () => application.InitializeAsync());
        exception.Message.ShouldContain("requires a transaction-capable replica set");
        exception.Message.ShouldContain("Detected Standalone");
    }

    private sealed class TestLocalDistributedEventBus : LocalDistributedEventBus
    {
        public TestLocalDistributedEventBus(
            IServiceScopeFactory serviceScopeFactory,
            ICurrentTenant currentTenant,
            IUnitOfWorkManager unitOfWorkManager,
            IOptions<AbpDistributedEventBusOptions> distributedEventBusOptions,
            IGuidGenerator guidGenerator,
            IClock clock,
            IEventHandlerInvoker eventHandlerInvoker,
            ILocalEventBus localEventBus,
            ICorrelationIdProvider correlationIdProvider)
            : base(
                serviceScopeFactory,
                currentTenant,
                unitOfWorkManager,
                distributedEventBusOptions,
                guidGenerator,
                clock,
                eventHandlerInvoker,
                localEventBus,
                correlationIdProvider)
        {
        }

        public Task<bool> AddToInboxForTestAsync(string messageId)
        {
            return AddToInboxAsync(
                messageId,
                "Dignite.Abp.NotificationCenter.MongoDB.Tests.InboxEto",
                typeof(InboxEto),
                new InboxEto { Value = "test" },
                correlationId: null);
        }
    }

    private sealed class InboxEto
    {
        public string Value { get; set; } = default!;
    }
}

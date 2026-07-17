using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.NotificationCenter.MongoDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MongoSandbox;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DistributedEvents;
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
        capability.TransactionProbeSucceeded.ShouldBeTrue();
        capability.MaxWireVersion.ShouldBeGreaterThanOrEqualTo(7);
    }

    [Fact]
    public async Task Host_lifecycle_validator_accepts_the_verified_replica_set()
    {
        var validator = ServiceProvider.GetServices<IHostedService>().Single(service =>
            service.GetType().Name == "NotificationCenterMongoDbOutboxCapabilityHostedService");

        await validator.StartAsync(CancellationToken.None);
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
            var messageIdIndex = incomingIndexes.Single(index =>
                index["key"].AsBsonDocument == new BsonDocument(nameof(IncomingEventRecord.MessageId), 1));
            messageIdIndex["unique"].AsBoolean.ShouldBeTrue();
        });
    }

    [Fact]
    public async Task Concurrent_inbox_redeliveries_leave_one_record_and_invoke_the_handler_once()
    {
        var messageId = Guid.NewGuid().ToString("N");
        var eventName = EventNameAttribute.GetNameOrDefault(typeof(MongoInboxEto));
        var eventData = JsonSerializer.SerializeToUtf8Bytes(new MongoInboxEto { Value = "test" });
        MongoInboxEtoHandler.Reset();

        var results = await Task.WhenAll(
            EnqueueIncomingAsync(messageId, eventName, eventData),
            EnqueueIncomingAsync(messageId, eventName, eventData));
        results.Count(result => result).ShouldBe(1);

        await WithUnitOfWorkAsync(async () =>
        {
            var dbContext = await GetRequiredService<IMongoDbContextProvider<NotificationCenterMongoDbContext>>()
                .GetDbContextAsync();
            var count = await dbContext.IncomingEvents.AsQueryable()
                .CountAsync(record => record.MessageId == messageId);
            count.ShouldBe(1);
        });

        var eventBusOptions = GetRequiredService<IOptions<AbpDistributedEventBusOptions>>().Value;
        var inboxConfig = eventBusOptions.Inboxes.Values.Single();
        await WithUnitOfWorkAsync(async () =>
        {
            var inbox = (IEventInbox)ServiceProvider.GetRequiredService(inboxConfig.ImplementationType);
            var waiting = await inbox.GetWaitingEventsAsync(10);
            waiting.Count.ShouldBe(1);

            await GetRequiredService<IDistributedEventBus>()
                .AsSupportsEventBoxes()
                .ProcessFromInboxAsync(waiting[0], inboxConfig);
            await inbox.MarkAsProcessedAsync(waiting[0].Id);
        }, isTransactional: true);

        MongoInboxEtoHandler.InvocationCount.ShouldBe(1);
        await WithUnitOfWorkAsync(async () =>
        {
            var inbox = (IEventInbox)ServiceProvider.GetRequiredService(inboxConfig.ImplementationType);
            (await inbox.GetWaitingEventsAsync(10)).ShouldBeEmpty();
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
        await application.InitializeAsync();

        var validator = application.ServiceProvider.GetServices<IHostedService>().Single(service =>
            service.GetType().Name == "NotificationCenterMongoDbOutboxCapabilityHostedService");

        var exception = await Should.ThrowAsync<AbpInitializationException>(
            () => validator.StartAsync(CancellationToken.None));
        exception.Message.ShouldContain("requires a verified transaction-capable replica set");
        exception.Message.ShouldContain("Detected Standalone");
        await application.ShutdownAsync();
    }

    private async Task<bool> EnqueueIncomingAsync(string messageId, string eventName, byte[] eventData)
    {
        try
        {
            using var unitOfWork = GetRequiredService<IUnitOfWorkManager>().Begin(
                requiresNew: true,
                isTransactional: true);
            var inboxConfig = GetRequiredService<IOptions<AbpDistributedEventBusOptions>>()
                .Value.Inboxes.Values.Single();
            var inbox = (IEventInbox)ServiceProvider.GetRequiredService(inboxConfig.ImplementationType);
            await inbox.EnqueueAsync(new IncomingEventInfo(
                Guid.NewGuid(),
                messageId,
                eventName,
                eventData,
                DateTime.UtcNow));
            await unitOfWork.CompleteAsync();
            return true;
        }
        catch (Exception exception) when (IsConcurrentDuplicate(exception))
        {
            // A broker redelivery retries after this loser transaction; ABP's MessageId check then
            // observes the winner. The unique index prevents two handler-visible inbox records.
            return false;
        }
    }

    private static bool IsConcurrentDuplicate(Exception exception)
    {
        if (exception is MongoWriteException writeException &&
            writeException.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return true;
        }

        if (exception is MongoCommandException commandException && commandException.Code == 11000)
        {
            return true;
        }

        if (exception is MongoException mongoException &&
            mongoException.HasErrorLabel("TransientTransactionError"))
        {
            return true;
        }

        return exception.InnerException != null && IsConcurrentDuplicate(exception.InnerException);
    }
}

public sealed class MongoInboxEto
{
    public string Value { get; set; } = default!;
}

public sealed class MongoInboxEtoHandler :
    IDistributedEventHandler<MongoInboxEto>,
    ITransientDependency
{
    private static int _invocationCount;

    public static int InvocationCount => Volatile.Read(ref _invocationCount);

    public static void Reset()
    {
        Volatile.Write(ref _invocationCount, 0);
    }

    public Task HandleEventAsync(MongoInboxEto eventData)
    {
        Interlocked.Increment(ref _invocationCount);
        return Task.CompletedTask;
    }
}

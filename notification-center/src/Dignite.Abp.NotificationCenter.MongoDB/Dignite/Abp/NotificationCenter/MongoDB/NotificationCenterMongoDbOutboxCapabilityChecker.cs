using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MongoDB;
using Volo.Abp.MongoDB.DistributedEvents;
using Volo.Abp.Uow;

namespace Dignite.Abp.NotificationCenter.MongoDB;

/// <summary>
/// Uses MongoDB's <c>hello</c> command to diagnose the transaction capability of the currently resolved
/// Notification Center database.
/// </summary>
public class NotificationCenterMongoDbOutboxCapabilityChecker :
    INotificationCenterMongoDbOutboxCapabilityChecker,
    ITransientDependency
{
    private const int ReplicaSetTransactionWireVersion = 7; // MongoDB 4.0

    protected IMongoDbContextProvider<NotificationCenterMongoDbContext> DbContextProvider { get; }

    protected IUnitOfWorkManager UnitOfWorkManager { get; }

    public NotificationCenterMongoDbOutboxCapabilityChecker(
        IMongoDbContextProvider<NotificationCenterMongoDbContext> dbContextProvider,
        IUnitOfWorkManager unitOfWorkManager)
    {
        DbContextProvider = dbContextProvider;
        UnitOfWorkManager = unitOfWorkManager;
    }

    /// <inheritdoc />
    public virtual async Task<NotificationCenterMongoDbOutboxCapability> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var unitOfWork = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await DbContextProvider.GetDbContextAsync(cancellationToken);
        var hello = await dbContext.Database.RunCommandAsync(
            new BsonDocumentCommand<BsonDocument>(new BsonDocument("hello", 1)),
            cancellationToken: cancellationToken);
        var topology = GetTopology(hello);
        var maxWireVersion = hello.TryGetValue("maxWireVersion", out var wireVersion)
            ? wireVersion.ToInt32()
            : 0;
        var supportsLogicalSessions = hello.TryGetValue("logicalSessionTimeoutMinutes", out var sessionTimeout) &&
                                      !sessionTimeout.IsBsonNull;

        if (topology != NotificationCenterMongoDbTopology.ReplicaSet ||
            !supportsLogicalSessions ||
            maxWireVersion < ReplicaSetTransactionWireVersion)
        {
            await unitOfWork.CompleteAsync(cancellationToken);
            var topologyNote = topology == NotificationCenterMongoDbTopology.ShardedCluster
                ? " Sharded clusters are diagnosed but are not enabled because this package does not test that topology."
                : string.Empty;
            return new NotificationCenterMongoDbOutboxCapability(
                false,
                topology,
                maxWireVersion,
                supportsLogicalSessions,
                false,
                $"Detected {topology}, logical sessions: {supportsLogicalSessions}, " +
                $"maxWireVersion: {maxWireVersion}, required replica-set wire version: " +
                $"{ReplicaSetTransactionWireVersion}.{topologyNote}");
        }

        try
        {
            await ProbeTransactionAsync(dbContext, cancellationToken);
            await unitOfWork.CompleteAsync(cancellationToken);
            return new NotificationCenterMongoDbOutboxCapability(
                true,
                topology,
                maxWireVersion,
                supportsLogicalSessions,
                true,
                $"Detected {topology} with logical sessions and maxWireVersion {maxWireVersion}; " +
                "the multi-collection transaction probe committed successfully.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new NotificationCenterMongoDbOutboxCapability(
                false,
                topology,
                maxWireVersion,
                supportsLogicalSessions,
                false,
                $"Detected {topology} with logical sessions and maxWireVersion {maxWireVersion}, " +
                $"but the multi-collection transaction probe failed: {exception.Message}");
        }
    }

    protected virtual async Task ProbeTransactionAsync(
        NotificationCenterMongoDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Accessing the typed collections initializes ABP's model and creates both collections/indexes
        // before the transaction starts (MongoDB 4.0 cannot create collections inside a transaction).
        var outboxName = dbContext.OutgoingEvents.CollectionNamespace.CollectionName;
        var inboxName = dbContext.IncomingEvents.CollectionNamespace.CollectionName;
        var outbox = dbContext.Database.GetCollection<BsonDocument>(outboxName);
        var inbox = dbContext.Database.GetCollection<BsonDocument>(inboxName);
        var probeId = ObjectId.GenerateNewId();
        var filter = Builders<BsonDocument>.Filter.Eq("_id", probeId);

        using var session = await dbContext.Client.StartSessionAsync(cancellationToken: cancellationToken);
        try
        {
            session.StartTransaction(new TransactionOptions(
                readConcern: ReadConcern.Snapshot,
                writeConcern: WriteConcern.WMajority));

            await outbox.InsertOneAsync(
                session,
                new BsonDocument { { "_id", probeId }, { "CapabilityProbe", true } },
                cancellationToken: cancellationToken);
            await inbox.InsertOneAsync(
                session,
                new BsonDocument
                {
                    { "_id", probeId },
                    { nameof(IncomingEventRecord.MessageId), "capability:" + probeId },
                    { "CapabilityProbe", true }
                },
                cancellationToken: cancellationToken);
            await outbox.DeleteOneAsync(session, filter, cancellationToken: cancellationToken);
            await inbox.DeleteOneAsync(session, filter, cancellationToken: cancellationToken);
            await session.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            if (session.IsInTransaction)
            {
                await session.AbortTransactionAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static NotificationCenterMongoDbTopology GetTopology(BsonDocument hello)
    {
        if (hello.TryGetValue("msg", out var message) && message.IsString && message.AsString == "isdbgrid")
        {
            return NotificationCenterMongoDbTopology.ShardedCluster;
        }

        return hello.TryGetValue("setName", out var setName) && !setName.IsBsonNull
            ? NotificationCenterMongoDbTopology.ReplicaSet
            : NotificationCenterMongoDbTopology.Standalone;
    }
}

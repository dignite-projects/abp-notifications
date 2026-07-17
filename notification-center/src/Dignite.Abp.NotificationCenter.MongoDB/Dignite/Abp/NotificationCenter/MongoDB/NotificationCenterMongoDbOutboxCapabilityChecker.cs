using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MongoDB;
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
    private const int ShardedTransactionWireVersion = 8; // MongoDB 4.2

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
        await unitOfWork.CompleteAsync(cancellationToken);

        return Evaluate(hello);
    }

    protected virtual NotificationCenterMongoDbOutboxCapability Evaluate(BsonDocument hello)
    {
        var topology = GetTopology(hello);
        var maxWireVersion = hello.TryGetValue("maxWireVersion", out var wireVersion)
            ? wireVersion.ToInt32()
            : 0;
        var supportsLogicalSessions = hello.TryGetValue("logicalSessionTimeoutMinutes", out var sessionTimeout) &&
                                      !sessionTimeout.IsBsonNull;
        var requiredWireVersion = topology == NotificationCenterMongoDbTopology.ShardedCluster
            ? ShardedTransactionWireVersion
            : ReplicaSetTransactionWireVersion;
        var isTransactionTopology = topology != NotificationCenterMongoDbTopology.Standalone;
        var isSupported = isTransactionTopology && supportsLogicalSessions && maxWireVersion >= requiredWireVersion;

        var diagnostic = isSupported
            ? $"Detected {topology} with logical sessions and maxWireVersion {maxWireVersion}."
            : $"Detected {topology}, logical sessions: {supportsLogicalSessions}, " +
              $"maxWireVersion: {maxWireVersion}, required: {requiredWireVersion}.";

        return new NotificationCenterMongoDbOutboxCapability(
            isSupported,
            topology,
            maxWireVersion,
            supportsLogicalSessions,
            diagnostic);
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

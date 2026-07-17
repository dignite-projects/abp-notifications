namespace Dignite.Abp.NotificationCenter.MongoDB;

/// <summary>MongoDB deployment topologies relevant to multi-document transaction support.</summary>
public enum NotificationCenterMongoDbTopology
{
    /// <summary>A standalone server, which cannot provide multi-document transactions.</summary>
    Standalone = 0,

    /// <summary>A replica set.</summary>
    ReplicaSet = 1,

    /// <summary>A sharded cluster reached through <c>mongos</c>.</summary>
    ShardedCluster = 2
}

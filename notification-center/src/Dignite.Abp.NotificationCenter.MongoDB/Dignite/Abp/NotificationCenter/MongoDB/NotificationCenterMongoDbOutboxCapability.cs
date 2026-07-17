namespace Dignite.Abp.NotificationCenter.MongoDB;

/// <summary>
/// Describes whether the configured MongoDB deployment can provide the transaction semantics required by
/// the Notification Center event outbox and inbox.
/// </summary>
public sealed class NotificationCenterMongoDbOutboxCapability
{
    /// <summary>Whether the deployment satisfies the outbox/inbox transaction prerequisites.</summary>
    public bool IsSupported { get; }

    /// <summary>The topology reported by MongoDB's <c>hello</c> command.</summary>
    public NotificationCenterMongoDbTopology Topology { get; }

    /// <summary>The maximum wire-protocol version reported by the deployment.</summary>
    public int MaxWireVersion { get; }

    /// <summary>Whether the deployment advertises logical-session support.</summary>
    public bool SupportsLogicalSessions { get; }

    /// <summary>A human-readable explanation of the capability decision.</summary>
    public string Diagnostic { get; }

    /// <summary>Creates an immutable capability result.</summary>
    public NotificationCenterMongoDbOutboxCapability(
        bool isSupported,
        NotificationCenterMongoDbTopology topology,
        int maxWireVersion,
        bool supportsLogicalSessions,
        string diagnostic)
    {
        IsSupported = isSupported;
        Topology = topology;
        MaxWireVersion = maxWireVersion;
        SupportsLogicalSessions = supportsLogicalSessions;
        Diagnostic = diagnostic;
    }
}

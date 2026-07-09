using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A notification delivery channel (SignalR, Email, Web Push, ...). The non-generic contract exposes channel
/// metadata so installed channels can be enumerated and notifications routed to specific ones.
/// </summary>
public interface INotificationNotifier
{
    /// <summary>Stable channel name used for routing (e.g. "SignalR", "Email").</summary>
    string Name { get; }
}

/// <summary>
/// A notification delivery channel that handles a specific distributed event type.
/// </summary>
public interface INotificationNotifier<in TEvent> :
    INotificationNotifier,
    IDistributedEventHandler<TEvent>
{
}

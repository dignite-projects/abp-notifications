namespace Dignite.Abp.Notifications;

/// <summary>
/// A notification delivery channel (SignalR, Email, Web Push, ...). A notifier implements this alongside its
/// distributed-event handler so the installed channels can be enumerated and notifications routed to specific ones.
/// </summary>
public interface INotificationNotifier
{
    /// <summary>Stable channel name used for routing (e.g. "SignalR", "Email").</summary>
    string Name { get; }
}

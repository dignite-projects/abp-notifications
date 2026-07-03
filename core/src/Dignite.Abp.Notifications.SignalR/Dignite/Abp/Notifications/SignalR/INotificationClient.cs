using System.Threading.Tasks;

namespace Dignite.Abp.Notifications.SignalR;

/// <summary>The strongly-typed client method invoked to deliver a real-time notification.</summary>
public interface INotificationClient
{
    Task ReceiveNotification(RealTimeNotification notification);
}

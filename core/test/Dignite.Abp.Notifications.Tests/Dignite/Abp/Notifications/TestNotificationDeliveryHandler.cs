using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Stands in for a real notifier: subscribes to <see cref="NotificationDeliveryEto"/> on the local distributed event bus
/// and records what it receives.
/// </summary>
public class TestNotificationDeliveryHandler : IDistributedEventHandler<NotificationDeliveryEto>, ITransientDependency
{
    private readonly ReceivedNotificationDeliveries _received;

    public TestNotificationDeliveryHandler(ReceivedNotificationDeliveries received)
    {
        _received = received;
    }

    public Task HandleEventAsync(NotificationDeliveryEto eventData)
    {
        _received.Items.Enqueue(eventData);
        return Task.CompletedTask;
    }
}

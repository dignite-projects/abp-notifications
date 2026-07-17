using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Records channel-specific work emitted through the local distributed event bus.
/// </summary>
[ExposeServices(
    typeof(IDistributedEventHandler<NotificationDeliveryWorkEto>),
    typeof(TestNotificationDeliveryHandler))]
public class TestNotificationDeliveryHandler :
    IDistributedEventHandler<NotificationDeliveryWorkEto>,
    ITransientDependency
{
    private readonly ReceivedNotificationDeliveries _received;

    public TestNotificationDeliveryHandler(ReceivedNotificationDeliveries received)
    {
        _received = received;
    }

    public Task HandleEventAsync(NotificationDeliveryWorkEto eventData)
    {
        _received.Items.Enqueue(eventData);
        return Task.CompletedTask;
    }
}

using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

[ExposeServices(
    typeof(IDistributedEventHandler<NotificationDeliveryRequestedEto>),
    typeof(NotificationDeliveryRequestedHandler))]
public class NotificationDeliveryRequestedHandler :
    IDistributedEventHandler<NotificationDeliveryRequestedEto>,
    ITransientDependency
{
    protected NotificationDeliveryProcessor Processor { get; }

    public NotificationDeliveryRequestedHandler(NotificationDeliveryProcessor processor)
    {
        Processor = processor;
    }

    public virtual Task HandleEventAsync(NotificationDeliveryRequestedEto eventData)
    {
        return Processor.ProcessAsync(eventData);
    }
}

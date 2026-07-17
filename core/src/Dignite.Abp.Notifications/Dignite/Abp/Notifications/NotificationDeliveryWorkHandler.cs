using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

[ExposeServices(
    typeof(IDistributedEventHandler<NotificationDeliveryWorkEto>),
    typeof(NotificationDeliveryWorkHandler))]
public class NotificationDeliveryWorkHandler :
    IDistributedEventHandler<NotificationDeliveryWorkEto>,
    ITransientDependency
{
    protected NotificationDeliveryProcessor Processor { get; }

    public NotificationDeliveryWorkHandler(NotificationDeliveryProcessor processor)
    {
        Processor = processor;
    }

    public virtual Task HandleEventAsync(NotificationDeliveryWorkEto eventData)
    {
        return Processor.ProcessAsync(eventData);
    }
}

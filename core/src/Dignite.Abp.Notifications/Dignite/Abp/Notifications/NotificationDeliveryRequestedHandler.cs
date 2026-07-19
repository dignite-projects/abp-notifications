using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Threading;

namespace Dignite.Abp.Notifications;

[ExposeServices(
    typeof(IDistributedEventHandler<NotificationDeliveryRequestedEto>),
    typeof(NotificationDeliveryRequestedHandler))]
public class NotificationDeliveryRequestedHandler :
    IDistributedEventHandler<NotificationDeliveryRequestedEto>,
    ITransientDependency
{
    protected NotificationDeliveryProcessor Processor { get; }
    protected ICancellationTokenProvider CancellationTokenProvider { get; }

    public NotificationDeliveryRequestedHandler(
        NotificationDeliveryProcessor processor,
        ICancellationTokenProvider cancellationTokenProvider)
    {
        Processor = processor;
        CancellationTokenProvider = cancellationTokenProvider;
    }

    public virtual Task HandleEventAsync(NotificationDeliveryRequestedEto eventData)
    {
        return Processor.ProcessAsync(eventData, CancellationTokenProvider.Token);
    }
}

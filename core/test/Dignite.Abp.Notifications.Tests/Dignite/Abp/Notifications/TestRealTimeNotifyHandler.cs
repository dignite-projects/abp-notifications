using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Stands in for a real notifier: subscribes to <see cref="RealTimeNotifyEto"/> on the local distributed event bus
/// and records what it receives.
/// </summary>
public class TestRealTimeNotifyHandler : IDistributedEventHandler<RealTimeNotifyEto>, ITransientDependency
{
    private readonly ReceivedRealTimeNotifications _received;

    public TestRealTimeNotifyHandler(ReceivedRealTimeNotifications received)
    {
        _received = received;
    }

    public Task HandleEventAsync(RealTimeNotifyEto eventData)
    {
        _received.Items.Enqueue(eventData);
        return Task.CompletedTask;
    }
}

using Shouldly;
using Volo.Abp.EventBus;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryContractTests
{
    [Fact]
    public void Delivery_request_keeps_the_existing_distributed_event_name()
    {
        EventNameAttribute.GetNameOrDefault(typeof(NotificationDeliveryRequestedEto))
            .ShouldBe("Dignite.Abp.Notifications.NotificationDeliveryWork");
    }

    [Theory]
    [InlineData(NotificationDeliveryState.Pending, 0)]
    [InlineData(NotificationDeliveryState.Processing, 1)]
    [InlineData(NotificationDeliveryState.Succeeded, 2)]
    [InlineData(NotificationDeliveryState.RetryScheduled, 3)]
    [InlineData(NotificationDeliveryState.Suppressed, 4)]
    [InlineData(NotificationDeliveryState.DeadLettered, 5)]
    public void Delivery_state_numeric_values_remain_storage_compatible(
        NotificationDeliveryState state,
        int persistedValue)
    {
        ((int)state).ShouldBe(persistedValue);
        ((NotificationDeliveryState)persistedValue).ShouldBe(state);
    }
}

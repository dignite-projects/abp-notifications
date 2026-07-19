using System;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryTrimmingTests
{
    [Fact]
    public void FromWorkItem_carries_content_but_not_an_aggregate_recipient_list()
    {
        var workItem = new NotificationDeliveryRequestedEto
        {
            NotificationId = Guid.NewGuid(),
            NotificationName = "test",
            Data = new MessageNotificationData("hi"),
            Severity = NotificationSeverity.Warn,
            CreationTime = DateTime.UtcNow,
            UserId = Guid.NewGuid(),
            Channel = "SignalR"
        };

        var payload = NotificationDelivery.FromWorkItem(workItem);

        // Compile-time + reflective proof that the per-user payload exposes no recipient list at all.
        typeof(NotificationDelivery).GetProperty("UserIds").ShouldBeNull();

        payload.NotificationId.ShouldBe(workItem.NotificationId);
        payload.NotificationName.ShouldBe("test");
        payload.Severity.ShouldBe(NotificationSeverity.Warn);
        payload.Data.ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hi");
    }
}

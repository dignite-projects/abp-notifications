using System;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryTrimmingTests
{
    [Fact]
    public void FromEto_carries_content_but_not_the_recipient_list()
    {
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(),
            "test",
            new MessageNotificationData("hi"),
            NotificationSeverity.Warn,
            DateTime.UtcNow,
            new[] { Guid.NewGuid(), Guid.NewGuid() });

        var payload = NotificationDelivery.FromEto(eto);

        // Compile-time + reflective proof that the per-user payload exposes no recipient list at all.
        typeof(NotificationDelivery).GetProperty("UserIds").ShouldBeNull();

        payload.NotificationId.ShouldBe(eto.NotificationId);
        payload.NotificationName.ShouldBe("test");
        payload.Severity.ShouldBe(NotificationSeverity.Warn);
        payload.Data.ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hi");
    }
}

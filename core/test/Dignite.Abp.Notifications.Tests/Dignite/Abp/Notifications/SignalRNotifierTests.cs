using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.SignalR;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace Dignite.Abp.Notifications;

public class SignalRNotifierTests
{
    [Fact]
    public async Task Pushes_a_trimmed_payload_to_all_target_users()
    {
        var clientProxy = Substitute.For<INotificationsClient>();
        var clients = Substitute.For<IHubClients<INotificationsClient>>();
        clients.Users(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationsHub, INotificationsClient>>();
        hubContext.Clients.Returns(clients);

        var notifier = new SignalRNotifier(hubContext);

        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "test", new MessageNotificationData("hi"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { u1, u2 })
        {
            Channels = new[] { SignalRNotifier.ChannelName }
        };

        await notifier.HandleEventAsync(eto);

        clients.Received(1).Users(Arg.Is<IReadOnlyList<string>>(
            l => l.Contains(u1.ToString()) && l.Contains(u2.ToString())));
        await clientProxy.Received(1).ReceiveNotification(Arg.Is<NotificationDelivery>(
            n => n.NotificationId == eto.NotificationId && n.NotificationName == "test"));
    }
}

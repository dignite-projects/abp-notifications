using System;
using System.Threading;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.SignalR;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Xunit;

namespace Dignite.Abp.Notifications;

public class SignalRNotifierTests
{
    [Fact]
    public async Task Pushes_one_trimmed_payload_to_the_requested_user_with_cancellation()
    {
        var clientProxy = Substitute.For<IClientProxy>();
        var clients = Substitute.For<IHubClients>();
        clients.User(Arg.Any<string>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationsHub>>();
        hubContext.Clients.Returns(clients);
        var notifier = new SignalRNotifier(hubContext);
        var cancellationToken = new CancellationTokenSource().Token;
        var userId = Guid.NewGuid();
        var request = new NotificationDeliveryRequestedEto
        {
            NotificationId = Guid.NewGuid(),
            NotificationName = "test",
            Data = new MessageNotificationData("hi"),
            Severity = NotificationSeverity.Info,
            CreationTime = DateTime.UtcNow,
            UserId = userId,
            Channel = SignalRNotifier.ChannelName
        };

        await notifier.DeliverAsync(request, cancellationToken);

        clients.Received(1).User(userId.ToString());
        await clientProxy.Received(1).SendCoreAsync(
            nameof(INotificationsClient.ReceiveNotification),
            Arg.Is<object[]>(arguments =>
                arguments.Length == 1
                && arguments[0] != null
                && ((NotificationDelivery)arguments[0]).NotificationId == request.NotificationId),
            cancellationToken);
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.SignalR;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Localization;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationChannelRouting_Tests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "SignalR" }, true)]
    [InlineData(new[] { "signalr" }, true)] // case-insensitive
    [InlineData(new[] { "Email" }, false)]
    [InlineData(new[] { "Email", "WebPush" }, false)]
    public void IsAllowed_respects_the_channel_white_list(string[]? channels, bool expected)
    {
        NotificationChannels.IsAllowed(channels, "SignalR").ShouldBe(expected);
    }

    [Fact]
    public void SignalR_notifier_exposes_its_channel_name()
    {
        var handler = new NotifyEventHandler(Substitute.For<IHubContext<NotificationsHub, INotificationClient>>());
        ((INotificationNotifier)handler).Name.ShouldBe(NotifyEventHandler.ChannelName);
    }

    [Fact]
    public async Task Handler_skips_when_its_channel_is_not_allowed()
    {
        var (handler, clients, clientProxy) = CreateHandler();

        await handler.HandleEventAsync(EtoWithChannels(new[] { "Email" }));

        clients.DidNotReceiveWithAnyArgs().Users(Arg.Any<IReadOnlyList<string>>());
        await clientProxy.DidNotReceiveWithAnyArgs().ReceiveNotification(Arg.Any<RealTimeNotification>());
    }

    [Fact]
    public async Task Handler_delivers_when_its_channel_is_allowed()
    {
        var (handler, _, clientProxy) = CreateHandler();

        await handler.HandleEventAsync(EtoWithChannels(new[] { "SignalR" }));

        await clientProxy.Received(1).ReceiveNotification(Arg.Any<RealTimeNotification>());
    }

    [Fact]
    public async Task Distributor_sets_eto_channels_from_the_definition()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();

        definitionManager.GetOrNull("test").Returns(
            new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("Email", "WebPush"));

        RealTimeNotifyEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<RealTimeNotifyEto>()))
            .Do(ci => published = ci.Arg<RealTimeNotifyEto>());

        var distributor = new DefaultNotificationDistributor(store, definitionManager, eventBus);
        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" }, new[] { Guid.NewGuid() });

        published.ShouldNotBeNull();
        published!.Channels.ShouldBe(new[] { "Email", "WebPush" });
    }

    private static RealTimeNotifyEto EtoWithChannels(string[] channels)
    {
        return new RealTimeNotifyEto(
            Guid.NewGuid(), "test", new MessageNotificationData("hi"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { Guid.NewGuid() })
        {
            Channels = channels
        };
    }

    private static (NotifyEventHandler handler, IHubClients<INotificationClient> clients, INotificationClient client)
        CreateHandler()
    {
        var clientProxy = Substitute.For<INotificationClient>();
        var clients = Substitute.For<IHubClients<INotificationClient>>();
        clients.Users(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationsHub, INotificationClient>>();
        hubContext.Clients.Returns(clients);
        return (new NotifyEventHandler(hubContext), clients, clientProxy);
    }
}

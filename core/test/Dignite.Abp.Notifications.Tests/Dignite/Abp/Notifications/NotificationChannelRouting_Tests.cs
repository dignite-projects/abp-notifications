using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Dignite.Abp.Notifications.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Localization;
using Volo.Abp.Reflection;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationChannelRouting_Tests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "SignalR" }, true)]
    [InlineData(new[] { "signalr" }, true)] // case-insensitive
    [InlineData(new[] { "Email" }, false)]
    [InlineData(new[] { "Email", "Other" }, false)]
    public void IsAllowed_respects_the_channel_white_list(string[]? channels, bool expected)
    {
        NotificationChannels.IsAllowed(channels, "SignalR").ShouldBe(expected);
    }

    [Fact]
    public void UseChannels_requires_at_least_one_channel()
    {
        Should.Throw<ArgumentException>(() =>
            new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels());
    }

    [Fact]
    public void UseChannels_rejects_blank_channel_names()
    {
        Should.Throw<ArgumentException>(() =>
            new NotificationDefinition("test", new FixedLocalizableString("Test")).UseChannels("SignalR", " "));
    }

    [Fact]
    public void SignalR_notifier_exposes_its_channel_name_and_event_contract()
    {
        var notifier = new SignalRNotifier(Substitute.For<IHubContext<NotificationsHub, INotificationsClient>>());

        ((INotificationNotifier)notifier).Name.ShouldBe(SignalRNotifier.ChannelName);
        (notifier is INotificationNotifier<NotificationDeliveryEto>).ShouldBeTrue();
        (notifier is IDistributedEventHandler<NotificationDeliveryEto>).ShouldBeTrue();
    }

    [Fact]
    public void Generic_notifier_contract_is_visible_to_abp_event_handler_scanning()
    {
        ReflectionHelper.IsAssignableToGenericType(
            typeof(SignalRNotifier),
            typeof(IDistributedEventHandler<>)).ShouldBeTrue();
        ReflectionHelper.IsAssignableToGenericType(
            typeof(EmailNotifier),
            typeof(IDistributedEventHandler<>)).ShouldBeTrue();
    }

    [Fact]
    public async Task SignalR_notifier_skips_when_its_channel_is_not_allowed()
    {
        var (handler, clients, clientProxy) = CreateHandler();

        await handler.HandleEventAsync(EtoWithChannels(new[] { "Email" }));

        clients.DidNotReceiveWithAnyArgs().Users(Arg.Any<IReadOnlyList<string>>());
        await clientProxy.DidNotReceiveWithAnyArgs().ReceiveNotification(Arg.Any<NotificationDelivery>());
    }

    [Fact]
    public async Task SignalR_notifier_delivers_when_its_channel_is_allowed()
    {
        var (handler, _, clientProxy) = CreateHandler();

        await handler.HandleEventAsync(EtoWithChannels(new[] { SignalRNotifier.ChannelName }));

        await clientProxy.Received(1).ReceiveNotification(Arg.Any<NotificationDelivery>());
    }

    [Fact]
    public async Task Distributor_sets_eto_channels_from_the_definition()
    {
        var store = Substitute.For<INotificationStore>();
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var eventBus = Substitute.For<IDistributedEventBus>();

        definitionManager.Get("test").Returns(
            new NotificationDefinition("test", new FixedLocalizableString("Test"))
                .UseChannels(EmailNotifier.ChannelName, SignalRNotifier.ChannelName));
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(true);

        NotificationDeliveryEto? published = null;
        eventBus.WhenForAnyArgs(x => x.PublishAsync(Arg.Any<NotificationDeliveryEto>()))
            .Do(ci => published = ci.Arg<NotificationDeliveryEto>());

        var currentTenant = new TestCurrentTenant();
        var distributor = new DefaultNotificationDistributor(
            store,
            definitionManager,
            eventBus,
            new DefaultNotificationRecipientEligibilityEvaluator(
                definitionManager,
                currentTenant,
                NullLogger<DefaultNotificationRecipientEligibilityEvaluator>.Instance),
            currentTenant,
            NullLogger<DefaultNotificationDistributor>.Instance);
        await distributor.DistributeAsync(
            new NotificationInfo { Id = Guid.NewGuid(), NotificationName = "test" }, new[] { Guid.NewGuid() });

        published.ShouldNotBeNull();
        published!.Channels.ShouldBe(new[] { EmailNotifier.ChannelName, SignalRNotifier.ChannelName });
    }

    private static NotificationDeliveryEto EtoWithChannels(string[] channels)
    {
        return new NotificationDeliveryEto(
            Guid.NewGuid(), "test", new MessageNotificationData("hi"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { Guid.NewGuid() })
        {
            Channels = channels
        };
    }

    private static (SignalRNotifier handler, IHubClients<INotificationsClient> clients, INotificationsClient client)
        CreateHandler()
    {
        var clientProxy = Substitute.For<INotificationsClient>();
        var clients = Substitute.For<IHubClients<INotificationsClient>>();
        clients.Users(Arg.Any<IReadOnlyList<string>>()).Returns(clientProxy);
        var hubContext = Substitute.For<IHubContext<NotificationsHub, INotificationsClient>>();
        hubContext.Clients.Returns(clients);
        return (new SignalRNotifier(hubContext), clients, clientProxy);
    }
}

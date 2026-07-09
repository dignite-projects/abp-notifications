using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using NSubstitute;
using Shouldly;
using Volo.Abp.Emailing;
using Volo.Abp.EventBus.Distributed;
using Xunit;

namespace Dignite.Abp.Notifications;

public class EmailNotifier_Tests
{
    [Fact]
    public void Email_notifier_exposes_its_channel_name_and_event_contract()
    {
        var notifier = new EmailNotifier(
            Substitute.For<IEmailSender>(),
            Substitute.For<IEmailNotificationAddressResolver>(),
            new DefaultNotificationEmailBuilder());

        ((INotificationNotifier)notifier).Name.ShouldBe(EmailNotifier.ChannelName);
        (notifier is INotificationNotifier<NotificationDeliveryEto>).ShouldBeTrue();
        (notifier is IDistributedEventHandler<NotificationDeliveryEto>).ShouldBeTrue();
    }

    [Fact]
    public async Task Emails_only_users_with_a_resolved_address()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var resolver = Substitute.For<IEmailNotificationAddressResolver>();
        var userWithEmail = Guid.NewGuid();
        var userWithoutEmail = Guid.NewGuid();
        resolver.GetEmailOrNullAsync(userWithEmail).Returns("a@b.com");
        resolver.GetEmailOrNullAsync(userWithoutEmail).Returns((string?)null);

        var notifier = new EmailNotifier(emailSender, resolver, new DefaultNotificationEmailBuilder());
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "order.shipped", new MessageNotificationData("Shipped!"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { userWithEmail, userWithoutEmail })
        {
            Channels = new[] { EmailNotifier.ChannelName }
        };

        await notifier.HandleEventAsync(eto);

        await emailSender.Received(1).SendAsync(
            Arg.Is<string>(to => to == "a@b.com"),
            Arg.Is<string>(subject => subject == "order.shipped"),
            Arg.Is<string>(body => body == "Shipped!"),
            Arg.Any<bool>());
        // Exactly one email total — the user without an address got nothing.
        await emailSender.Received(1).SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Skips_when_the_email_channel_is_not_allowed()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var resolver = Substitute.For<IEmailNotificationAddressResolver>();
        resolver.GetEmailOrNullAsync(Arg.Any<Guid>()).Returns("a@b.com");

        var notifier = new EmailNotifier(emailSender, resolver, new DefaultNotificationEmailBuilder());
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "test", new MessageNotificationData("x"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { Guid.NewGuid() })
        {
            Channels = new[] { "SignalR" } // Email channel excluded
        };

        await notifier.HandleEventAsync(eto);

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }
}

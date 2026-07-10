using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.Emailing;
using Volo.Abp.EventBus.Distributed;
using Xunit;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

public class EmailNotifier_Tests
{
    [Fact]
    public void Email_notifier_exposes_its_channel_name_and_event_contract()
    {
        var notifier = new EmailNotifier(
            Substitute.For<IEmailSender>(),
            Substitute.For<IEmailNotificationAddressResolver>(),
            CreateDefaultBuilder(),
            CreateCurrentTenant(),
            NullLogger<EmailNotifier>.Instance);

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

        var notifier = new EmailNotifier(
            emailSender,
            resolver,
            CreateDefaultBuilder(),
            CreateCurrentTenant(),
            NullLogger<EmailNotifier>.Instance);
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

        var notifier = new EmailNotifier(
            emailSender,
            resolver,
            CreateDefaultBuilder(),
            CreateCurrentTenant(),
            NullLogger<EmailNotifier>.Instance);
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

    [Fact]
    public async Task Skips_when_no_provider_can_build_email_content()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var resolver = Substitute.For<IEmailNotificationAddressResolver>();
        resolver.GetEmailOrNullAsync(Arg.Any<Guid>()).Returns("a@b.com");
        var notifier = new EmailNotifier(
            emailSender,
            resolver,
            CreateDefaultBuilder(),
            CreateCurrentTenant(),
            NullLogger<EmailNotifier>.Instance);

        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "order.shipped", new OrderShippedNotificationData(),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { Guid.NewGuid() })
        {
            Channels = new[] { EmailNotifier.ChannelName }
        };

        await notifier.HandleEventAsync(eto);

        await emailSender.DidNotReceiveWithAnyArgs().SendAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task Builds_email_content_per_resolved_recipient()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var resolver = Substitute.For<IEmailNotificationAddressResolver>();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var builder = new CapturingEmailBuilder();
        resolver.GetEmailOrNullAsync(firstUserId).Returns("first@example.com");
        resolver.GetEmailOrNullAsync(secondUserId).Returns("second@example.com");

        var notifier = new EmailNotifier(
            emailSender,
            resolver,
            builder,
            CreateCurrentTenant(),
            NullLogger<EmailNotifier>.Instance);
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "test", new MessageNotificationData("x"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { firstUserId, secondUserId })
        {
            Channels = new[] { EmailNotifier.ChannelName },
            TenantId = Guid.NewGuid()
        };

        await notifier.HandleEventAsync(eto);

        builder.Contexts.Select(context => context.UserId).ShouldBe(new[] { firstUserId, secondUserId });
        builder.Contexts.Select(context => context.EmailAddress).ShouldBe(new[] { "first@example.com", "second@example.com" });
        builder.Contexts.ShouldAllBe(context => context.TenantId == eto.TenantId);
    }

    [Fact]
    public async Task Resolves_addresses_inside_the_event_tenant_context()
    {
        var emailSender = Substitute.For<IEmailSender>();
        var resolver = Substitute.For<IEmailNotificationAddressResolver>();
        var currentTenant = new TestCurrentTenant();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        Guid? tenantSeenByResolver = null;

        resolver.GetEmailOrNullAsync(userId).Returns(_ =>
        {
            tenantSeenByResolver = currentTenant.Id;
            return Task.FromResult<string?>("tenant@example.com");
        });

        var notifier = new EmailNotifier(
            emailSender,
            resolver,
            CreateDefaultBuilder(),
            currentTenant,
            NullLogger<EmailNotifier>.Instance);
        var eto = new NotificationDeliveryEto(
            Guid.NewGuid(), "test", new MessageNotificationData("x"),
            NotificationSeverity.Info, DateTime.UtcNow, new[] { userId })
        {
            Channels = new[] { EmailNotifier.ChannelName },
            TenantId = tenantId
        };

        await notifier.HandleEventAsync(eto);

        tenantSeenByResolver.ShouldBe(tenantId);
        currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Default_builder_uses_provider_order_and_allows_business_providers_before_built_in_fallbacks()
    {
        var businessProvider = new StaticEmailContentProvider(
            NotificationEmailContentProviderOrders.Default,
            _ => new NotificationEmail("business", "custom"));
        var builder = new DefaultNotificationEmailBuilder(new INotificationEmailContentProvider[]
        {
            new MessageNotificationEmailContentProvider(),
            businessProvider
        });

        var email = await builder.BuildAsync(CreateContext(new MessageNotificationData("built-in")));

        email.ShouldNotBeNull();
        email.Subject.ShouldBe("business");
        email.Body.ShouldBe("custom");
    }

    [Fact]
    public async Task Default_builder_returns_first_non_null_provider_by_order()
    {
        var laterProvider = new StaticEmailContentProvider(
            20,
            _ => new NotificationEmail("later", "later"));
        var earlierProvider = new StaticEmailContentProvider(
            10,
            _ => new NotificationEmail("earlier", "earlier"));
        var builder = new DefaultNotificationEmailBuilder(new INotificationEmailContentProvider[]
        {
            laterProvider,
            earlierProvider
        });

        var email = await builder.BuildAsync(CreateContext(new OrderShippedNotificationData()));

        email.ShouldNotBeNull();
        email.Subject.ShouldBe("earlier");
    }

    [Fact]
    public async Task Default_builder_supports_localizable_message_notification_data()
    {
        var builder = new DefaultNotificationEmailBuilder(new INotificationEmailContentProvider[]
        {
            new LocalizableMessageNotificationEmailContentProvider(new TestStringLocalizerFactory())
        });
        var data = new LocalizableMessageNotificationData(resourceName: null!, name: "OrderShipped")
        {
            Arguments = new Dictionary<string, object> { ["OrderNo"] = "A-42" }
        };

        var email = await builder.BuildAsync(CreateContext(data));

        email.ShouldNotBeNull();
        email.Subject.ShouldBe("test.notification");
        email.Body.ShouldBe("OrderShipped");
    }

    private static DefaultNotificationEmailBuilder CreateDefaultBuilder()
    {
        return new DefaultNotificationEmailBuilder(new INotificationEmailContentProvider[]
        {
            new MessageNotificationEmailContentProvider(),
            new LocalizableMessageNotificationEmailContentProvider(new TestStringLocalizerFactory())
        });
    }

    private static ICurrentTenant CreateCurrentTenant()
    {
        return new TestCurrentTenant();
    }

    private static NotificationEmailBuildContext CreateContext(NotificationData data)
    {
        return new NotificationEmailBuildContext(
            new NotificationDelivery
            {
                NotificationId = Guid.NewGuid(),
                NotificationName = "test.notification",
                Data = data,
                Severity = NotificationSeverity.Info,
                CreationTime = DateTime.UtcNow
            },
            Guid.NewGuid(),
            "a@b.com",
            tenantId: null);
    }

    private class StaticEmailContentProvider : INotificationEmailContentProvider
    {
        private readonly Func<NotificationEmailBuildContext, NotificationEmail?> _build;

        public int Order { get; }

        public StaticEmailContentProvider(
            int order,
            Func<NotificationEmailBuildContext, NotificationEmail?> build)
        {
            Order = order;
            _build = build;
        }

        public Task<NotificationEmail?> BuildOrNullAsync(NotificationEmailBuildContext context)
        {
            return Task.FromResult(_build(context));
        }
    }

    private class CapturingEmailBuilder : INotificationEmailBuilder
    {
        public List<NotificationEmailBuildContext> Contexts { get; } = new();

        public Task<NotificationEmail?> BuildAsync(NotificationEmailBuildContext context)
        {
            Contexts.Add(context);
            return Task.FromResult<NotificationEmail?>(
                new NotificationEmail(context.Notification.NotificationName, context.UserId.ToString()));
        }
    }

    private class TestStringLocalizerFactory : IStringLocalizerFactory
    {
        private readonly IStringLocalizer _localizer = new TestStringLocalizer();

        public IStringLocalizer Create(Type resourceSource)
        {
            return _localizer;
        }

        public IStringLocalizer Create(string baseName, string location)
        {
            return _localizer;
        }
    }

    private class TestStringLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, $"localized:{name}");

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(
                CultureInfo.InvariantCulture,
                "localized:{0}:{1}",
                new object[] { name, string.Join(",", arguments.Select(x => x?.ToString())) }));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Enumerable.Empty<LocalizedString>();
        }
    }

    private class TestCurrentTenant : ICurrentTenant
    {
        public bool IsAvailable => Id.HasValue;

        public Guid? Id { get; private set; }

        public string? Name { get; private set; }

        public IDisposable Change(Guid? id, string? name = null)
        {
            var previousId = Id;
            var previousName = Name;
            Id = id;
            Name = name;
            return new DisposeAction(() =>
            {
                Id = previousId;
                Name = previousName;
            });
        }
    }

    private class DisposeAction : IDisposable
    {
        private readonly Action _dispose;

        public DisposeAction(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            _dispose();
        }
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Boots the real ABP module and exercises the wired-up pipeline end to end — this is what verifies module
/// DependsOn, DI registration, definition-provider auto-discovery, and (crucially) that the singleton
/// definition manager resolves request-scoped services without a captive-dependency problem (fix B).
/// </summary>
public class NotificationDistribution_Integration_Tests : DigniteAbpNotificationsTestBase
{
    private readonly INotificationPublisher _publisher;
    private readonly INotificationDefinitionManager _definitionManager;
    private readonly ReceivedNotificationDeliveries _received;

    public NotificationDistribution_Integration_Tests()
    {
        _publisher = GetRequiredService<INotificationPublisher>();
        _definitionManager = GetRequiredService<INotificationDefinitionManager>();
        _received = GetRequiredService<ReceivedNotificationDeliveries>();
    }

    [Fact]
    public async Task Publishing_to_explicit_users_emits_a_delivery_eto_through_the_bus()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.Plain,
            new MessageNotificationData("hi"),
            userIds: new[] { u1, u2 });

        _received.Items.Count.ShouldBe(1);
        var eto = _received.Items.Single();
        eto.NotificationName.ShouldBe(TestNotificationDefinitionProvider.Plain);
        eto.UserIds.ShouldContain(u1);
        eto.UserIds.ShouldContain(u2);
        eto.Data.ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hi");
    }

    [Fact]
    public void Definition_providers_are_auto_discovered()
    {
        _definitionManager.GetOrNull(TestNotificationDefinitionProvider.Plain).ShouldNotBeNull();
        _definitionManager.GetAll().Select(d => d.Name)
            .ShouldContain(TestNotificationDefinitionProvider.FeatureGated);
    }

    [Fact]
    public async Task Availability_reflects_feature_gating()
    {
        var userId = Guid.NewGuid();

        (await _definitionManager.IsAvailableAsync(TestNotificationDefinitionProvider.Plain, userId))
            .ShouldBeTrue();
        (await _definitionManager.IsAvailableAsync(TestNotificationDefinitionProvider.FeatureGated, userId))
            .ShouldBeTrue();
        (await _definitionManager.IsAvailableAsync(TestNotificationDefinitionProvider.DisabledFeatureGated, userId))
            .ShouldBeFalse();
    }
}

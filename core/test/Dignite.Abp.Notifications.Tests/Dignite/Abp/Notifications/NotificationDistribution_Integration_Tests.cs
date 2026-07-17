using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.MultiTenancy;
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
    private readonly FakeBackgroundJobManager _backgroundJobs;
    private readonly ICurrentTenant _currentTenant;

    public NotificationDistribution_Integration_Tests()
    {
        _publisher = GetRequiredService<INotificationPublisher>();
        _definitionManager = GetRequiredService<INotificationDefinitionManager>();
        _received = GetRequiredService<ReceivedNotificationDeliveries>();
        _backgroundJobs = GetRequiredService<FakeBackgroundJobManager>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    [Fact]
    public async Task Publishing_to_explicit_users_emits_a_delivery_eto_through_the_bus()
    {
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.Plain,
            new MessageNotificationData("hi"),
            userIds: new[] { u1, u1, u2 });

        _received.Items.Count.ShouldBe(1);
        var eto = _received.Items.Single();
        eto.NotificationName.ShouldBe(TestNotificationDefinitionProvider.Plain);
        eto.UserIds.ShouldBe(new[] { u1, u2 });
        eto.Data.ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hi");
    }

    [Fact]
    public async Task Publishing_an_empty_explicit_list_is_a_no_op_in_core_only_mode()
    {
        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.Plain,
            new MessageNotificationData("hi"),
            userIds: Array.Empty<Guid>());

        _received.Items.ShouldBeEmpty();
        _backgroundJobs.EnqueuedArgs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Publishing_null_recipients_resolves_no_subscribers_on_the_core_only_background_path()
    {
        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.Plain,
            new MessageNotificationData("hi"),
            userIds: null);

        var args = _backgroundJobs.EnqueuedArgs.Single().ShouldBeOfType<NotificationDistributionJobArgs>();
        args.UserIds.ShouldBeNull();
        await GetRequiredService<NotificationDistributionJob>().ExecuteAsync(args);

        _received.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Publishing_above_the_threshold_delivers_explicit_users_on_the_core_only_background_path()
    {
        var users = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.Plain,
            new MessageNotificationData("hi"),
            userIds: users);

        var args = _backgroundJobs.EnqueuedArgs.Single().ShouldBeOfType<NotificationDistributionJobArgs>();
        await GetRequiredService<NotificationDistributionJob>().ExecuteAsync(args);

        _received.Items.Single().UserIds.ShouldBe(users);
    }

    [Fact]
    public async Task Explicit_recipients_must_satisfy_permission_requirements_by_default()
    {
        var userId = Guid.NewGuid();

        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.PermissionDenied,
            userIds: new[] { userId });

        _received.Items.ShouldBeEmpty();

        await _publisher.PublishAsync(
            TestNotificationDefinitionProvider.PermissionGranted,
            userIds: new[] { userId });

        _received.Items.Single().NotificationName
            .ShouldBe(TestNotificationDefinitionProvider.PermissionGranted);
    }

    [Fact]
    public async Task Explicit_recipients_must_satisfy_features_in_the_notification_tenant_by_default()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId, "tenant"))
        {
            await _publisher.PublishAsync(
                TestNotificationDefinitionProvider.DisabledFeatureGated,
                userIds: new[] { userId });

            _received.Items.ShouldBeEmpty();

            await _publisher.PublishAsync(
                TestNotificationDefinitionProvider.FeatureGated,
                userIds: new[] { userId });
        }

        var delivery = _received.Items.Single();
        delivery.NotificationName.ShouldBe(TestNotificationDefinitionProvider.FeatureGated);
        delivery.TenantId.ShouldBe(tenantId);
        _currentTenant.Id.ShouldBeNull();
    }

    [Fact]
    public async Task Named_bypass_delivers_a_trusted_system_notification_and_is_tenant_scoped()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using (_currentTenant.Change(tenantId, "tenant"))
        {
            await _publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
                TestNotificationDefinitionProvider.DisabledFeatureGated,
                new[] { userId },
                new MessageNotificationData("system"));
        }

        var delivery = _received.Items.Single();
        delivery.UserIds.ShouldBe(new[] { userId });
        delivery.TenantId.ShouldBe(tenantId);
    }

    [Fact]
    public async Task Background_job_preserves_the_named_bypass_for_explicit_recipients()
    {
        var tenantId = Guid.NewGuid();
        var users = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

        using (_currentTenant.Change(tenantId, "tenant"))
        {
            await _publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
                TestNotificationDefinitionProvider.DisabledFeatureGated,
                users);
        }

        var args = _backgroundJobs.EnqueuedArgs.Single().ShouldBeOfType<NotificationDistributionJobArgs>();
        args.RecipientEligibilityMode.ShouldBe(
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements);
        await GetRequiredService<NotificationDistributionJob>().ExecuteAsync(args);

        var delivery = _received.Items.Single();
        delivery.UserIds.ShouldBe(users);
        delivery.TenantId.ShouldBe(tenantId);
        _currentTenant.Id.ShouldBeNull();
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

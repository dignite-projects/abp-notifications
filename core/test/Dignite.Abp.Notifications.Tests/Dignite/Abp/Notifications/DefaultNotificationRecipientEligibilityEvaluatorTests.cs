using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

public class DefaultNotificationRecipientEligibilityEvaluatorTests
{
    [Fact]
    public async Task Evaluation_filters_denied_users_in_the_notification_tenant_and_restores_the_caller_context()
    {
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var currentTenant = new TestCurrentTenant();
        var logger = Substitute.For<ILogger<DefaultNotificationRecipientEligibilityEvaluator>>();
        var evaluator = new DefaultNotificationRecipientEligibilityEvaluator(
            definitionManager,
            currentTenant,
            logger);
        var notificationTenantId = Guid.NewGuid();
        var callerTenantId = Guid.NewGuid();
        var eligible = Guid.NewGuid();
        var denied = Guid.NewGuid();
        var tenantsSeen = new List<Guid?>();
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(call =>
        {
            tenantsSeen.Add(currentTenant.Id);
            return call.Arg<Guid>() == eligible;
        });

        using (currentTenant.Change(callerTenantId, "caller"))
        {
            var result = await evaluator.EvaluateAsync(
                "test",
                new[] { eligible, denied },
                notificationTenantId,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);

            result.EligibleUserIds.ShouldBe(new[] { eligible });
            result.ExcludedUserIds.ShouldBe(new[] { denied });
            tenantsSeen.ShouldAllBe(tenantId => tenantId == notificationTenantId);
            currentTenant.Id.ShouldBe(callerTenantId);
        }

        logger.ReceivedCalls().Any(call =>
            Equals(call.GetArguments().FirstOrDefault(), LogLevel.Information)).ShouldBeTrue();
    }

    [Fact]
    public async Task Host_evaluation_cannot_inherit_an_ambient_tenant()
    {
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var currentTenant = new TestCurrentTenant();
        var evaluator = new DefaultNotificationRecipientEligibilityEvaluator(
            definitionManager,
            currentTenant,
            Substitute.For<ILogger<DefaultNotificationRecipientEligibilityEvaluator>>());
        Guid? tenantSeen = Guid.NewGuid();
        definitionManager.IsAvailableAsync("test", Arg.Any<Guid>()).Returns(_ =>
        {
            tenantSeen = currentTenant.Id;
            return true;
        });
        var callerTenantId = Guid.NewGuid();

        using (currentTenant.Change(callerTenantId, "caller"))
        {
            await evaluator.EvaluateAsync(
                "test",
                new[] { Guid.NewGuid() },
                tenantId: null,
                NotificationRecipientEligibilityMode.EnforceDefinitionRequirements);

            tenantSeen.ShouldBeNull();
            currentTenant.Id.ShouldBe(callerTenantId);
        }
    }

    [Fact]
    public async Task Bypass_is_observable_and_skips_definition_checks()
    {
        var definitionManager = Substitute.For<INotificationDefinitionManager>();
        var logger = Substitute.For<ILogger<DefaultNotificationRecipientEligibilityEvaluator>>();
        var evaluator = new DefaultNotificationRecipientEligibilityEvaluator(
            definitionManager,
            new TestCurrentTenant(),
            logger);
        var userIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await evaluator.EvaluateAsync(
            "test",
            userIds,
            Guid.NewGuid(),
            NotificationRecipientEligibilityMode.BypassDefinitionRequirements);

        result.EligibleUserIds.ShouldBe(userIds);
        result.ExcludedUserIds.ShouldBeEmpty();
        await definitionManager.DidNotReceiveWithAnyArgs().IsAvailableAsync(default!, default);
        logger.ReceivedCalls().Any(call =>
            Equals(call.GetArguments().FirstOrDefault(), LogLevel.Warning)).ShouldBeTrue();
    }

    [Fact]
    public async Task Unknown_policy_mode_is_rejected()
    {
        var evaluator = new DefaultNotificationRecipientEligibilityEvaluator(
            Substitute.For<INotificationDefinitionManager>());

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => evaluator.EvaluateAsync(
            "test",
            new[] { Guid.NewGuid() },
            null,
            (NotificationRecipientEligibilityMode)42));
    }
}

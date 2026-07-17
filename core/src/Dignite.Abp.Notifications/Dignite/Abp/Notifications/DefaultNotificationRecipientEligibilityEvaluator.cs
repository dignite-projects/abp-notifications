using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Default recipient policy: definition requirements govern both subscription and delivery eligibility.
/// Denied candidates are filtered with diagnostics. The distributor independently audits the narrowly named
/// explicit-recipient bypass so replacing this policy cannot suppress that warning.
/// </summary>
public class DefaultNotificationRecipientEligibilityEvaluator :
    INotificationRecipientEligibilityEvaluator,
    ITransientDependency
{
    protected INotificationDefinitionManager DefinitionManager { get; }

    protected ICurrentTenant CurrentTenant { get; }

    protected ILogger<DefaultNotificationRecipientEligibilityEvaluator> Logger { get; }

    public DefaultNotificationRecipientEligibilityEvaluator(
        INotificationDefinitionManager definitionManager,
        ICurrentTenant currentTenant,
        ILogger<DefaultNotificationRecipientEligibilityEvaluator> logger)
    {
        DefinitionManager = definitionManager;
        CurrentTenant = currentTenant;
        Logger = logger;
    }

    public virtual async Task<NotificationRecipientEligibilityResult> EvaluateAsync(
        string notificationName,
        IReadOnlyCollection<Guid> candidateUserIds,
        Guid? tenantId,
        NotificationRecipientEligibilityMode mode)
    {
        ArgumentNullException.ThrowIfNull(candidateUserIds);

        var candidates = candidateUserIds.Distinct().ToList();
        if (mode == NotificationRecipientEligibilityMode.BypassDefinitionRequirements)
        {
            return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
        }

        if (mode != NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown recipient eligibility mode.");
        }

        using var tenantChange = CurrentTenant.Change(tenantId, null);
        var eligible = new List<Guid>(candidates.Count);
        var excluded = new List<Guid>();
        foreach (var userId in candidates)
        {
            if (await DefinitionManager.IsAvailableAsync(notificationName, userId))
            {
                eligible.Add(userId);
            }
            else
            {
                excluded.Add(userId);
            }
        }

        if (excluded.Count > 0)
        {
            Logger.LogInformation(
                "Excluded {ExcludedRecipientCount} of {CandidateRecipientCount} recipients from notification " +
                "'{NotificationName}' because its definition requirements were not satisfied in tenant {TenantId}.",
                excluded.Count,
                candidates.Count,
                notificationName,
                tenantId);
            Logger.LogDebug(
                "Recipients excluded from notification '{NotificationName}': {ExcludedUserIds}.",
                notificationName,
                string.Join(", ", excluded));
        }

        return new NotificationRecipientEligibilityResult(eligible, excluded);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Default recipient policy: definition requirements govern both subscription and delivery eligibility.
/// Denied candidates are filtered, while the narrowly named explicit-recipient bypass is logged.
/// </summary>
public class DefaultNotificationRecipientEligibilityEvaluator :
    INotificationRecipientEligibilityEvaluator,
    ITransientDependency
{
    protected INotificationDefinitionManager DefinitionManager { get; }

    protected ICurrentTenant? CurrentTenant { get; }

    protected ILogger<DefaultNotificationRecipientEligibilityEvaluator> Logger { get; }

    /// <summary>
    /// Preserves manual construction compatibility. DI uses the fuller constructor so tenant switching and logs
    /// are active; manual callers still enforce requirements in their ambient tenant.
    /// </summary>
    public DefaultNotificationRecipientEligibilityEvaluator(INotificationDefinitionManager definitionManager)
        : this(
            definitionManager,
            null,
            NullLogger<DefaultNotificationRecipientEligibilityEvaluator>.Instance)
    {
    }

    public DefaultNotificationRecipientEligibilityEvaluator(
        INotificationDefinitionManager definitionManager,
        ICurrentTenant? currentTenant,
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
            Logger.LogWarning(
                "Bypassing notification definition requirements for {RecipientCount} explicit recipients of " +
                "'{NotificationName}' in tenant {TenantId}.",
                candidates.Count,
                notificationName,
                tenantId);

            return new NotificationRecipientEligibilityResult(candidates, Array.Empty<Guid>());
        }

        if (mode != NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown recipient eligibility mode.");
        }

        using var tenantChange = CurrentTenant?.Change(tenantId, null);
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

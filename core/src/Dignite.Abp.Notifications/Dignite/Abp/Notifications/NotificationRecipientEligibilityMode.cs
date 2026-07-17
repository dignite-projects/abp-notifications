namespace Dignite.Abp.Notifications;

/// <summary>Controls whether notification definition requirements are enforced for recipient selection.</summary>
public enum NotificationRecipientEligibilityMode
{
    /// <summary>Require every candidate to satisfy the definition's feature and permission requirements.</summary>
    EnforceDefinitionRequirements = 0,

    /// <summary>
    /// Deliberately skip definition requirements for explicitly targeted trusted-system notifications.
    /// This mode is not valid for subscription-derived recipients.
    /// </summary>
    BypassDefinitionRequirements = 1
}

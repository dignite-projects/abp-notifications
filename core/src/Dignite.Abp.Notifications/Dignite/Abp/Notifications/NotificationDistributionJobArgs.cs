using System;

namespace Dignite.Abp.Notifications;

[Serializable]
public class NotificationDistributionJobArgs
{
    public NotificationInfo Notification { get; set; } = default!;

    /// <summary>
    /// <see langword="null"/> selects subscription resolution; an empty array is an intentional no-op; a
    /// non-empty array contains explicit recipients.
    /// </summary>
    public Guid[]? UserIds { get; set; }

    public Guid[]? ExcludedUserIds { get; set; }

    /// <summary>
    /// Defaults to enforcement, so jobs serialized before this property existed adopt the secure behavior.
    /// </summary>
    public NotificationRecipientEligibilityMode RecipientEligibilityMode { get; set; }

    public NotificationDistributionJobArgs()
    {
    }

    public NotificationDistributionJobArgs(NotificationInfo notification, Guid[]? userIds, Guid[]? excludedUserIds)
        : this(
            notification,
            userIds,
            excludedUserIds,
            NotificationRecipientEligibilityMode.EnforceDefinitionRequirements)
    {
    }

    public NotificationDistributionJobArgs(
        NotificationInfo notification,
        Guid[]? userIds,
        Guid[]? excludedUserIds,
        NotificationRecipientEligibilityMode recipientEligibilityMode)
    {
        Notification = notification;
        UserIds = userIds;
        ExcludedUserIds = excludedUserIds;
        RecipientEligibilityMode = recipientEligibilityMode;
    }
}

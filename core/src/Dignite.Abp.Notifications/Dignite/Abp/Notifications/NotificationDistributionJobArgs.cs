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

    /// <summary>
    /// Indicates that the publisher prepared the shared notification record before splitting explicit recipients
    /// into bounded jobs. Defaults to <see langword="false"/> for jobs serialized by earlier versions.
    /// </summary>
    public bool NotificationAlreadyPersisted { get; set; }

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
        NotificationRecipientEligibilityMode recipientEligibilityMode,
        bool notificationAlreadyPersisted = false)
    {
        Notification = notification;
        UserIds = userIds;
        ExcludedUserIds = excludedUserIds;
        RecipientEligibilityMode = recipientEligibilityMode;
        NotificationAlreadyPersisted = notificationAlreadyPersisted;
    }
}

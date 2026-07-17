namespace Dignite.Abp.Notifications;

/// <summary>Defines whether a published notification may carry a stable entity identity.</summary>
public enum NotificationEntityRequirement
{
    /// <summary>
    /// No entity contract is declared. This preserves the compatibility behavior of existing definitions.
    /// </summary>
    Unspecified = 0,

    /// <summary>An entity identity must not be supplied.</summary>
    Forbidden = 1,

    /// <summary>An entity identity may be supplied.</summary>
    Optional = 2,

    /// <summary>An entity identity must be supplied.</summary>
    Required = 3
}

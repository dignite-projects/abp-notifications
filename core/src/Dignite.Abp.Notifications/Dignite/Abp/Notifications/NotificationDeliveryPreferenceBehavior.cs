namespace Dignite.Abp.Notifications;

/// <summary>Controls whether a definition is subject to user delivery preferences.</summary>
public enum NotificationDeliveryPreferenceBehavior
{
    RespectPreferences = 0,

    /// <summary>Deliver immediately, bypassing both permanent opt-outs and quiet hours.</summary>
    Mandatory = 1
}

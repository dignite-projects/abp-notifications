namespace Dignite.Abp.Notifications;

/// <summary>Stable diagnostic codes carried with producer-resolved delivery preference decisions.</summary>
public static class NotificationDeliveryPreferenceReasonCodes
{
    public const int MaxLength = 64;

    public const string UserOptOut = "user-opt-out";

    public const string QuietHours = "quiet-hours";
}

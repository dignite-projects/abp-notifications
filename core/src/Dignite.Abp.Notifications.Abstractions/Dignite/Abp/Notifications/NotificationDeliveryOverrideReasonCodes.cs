namespace Dignite.Abp.Notifications;

/// <summary>Stable, non-sensitive audit reason codes for explicit delivery overrides.</summary>
public static class NotificationDeliveryOverrideReasonCodes
{
    public const int MaxLength = 64;

    public const string OperatorForceDelivery = "operator-force-delivery";
}

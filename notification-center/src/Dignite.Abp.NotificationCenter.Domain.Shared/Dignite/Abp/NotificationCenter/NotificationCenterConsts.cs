using Dignite.Abp.Notifications;

namespace Dignite.Abp.NotificationCenter;

public static class NotificationCenterConsts
{
    public const int MaxNotificationNameLength = 256;

    public const int MaxEntityTypeNameLength = 512;

    public const int MaxEntityIdLength = 128;

    public const int SubscriptionIdentityKeyLength = 64;

    public const int MaxDeliveryChannelLength = NotificationDeliveryIdentity.MaxChannelNameLength;

    public const int DeliveryIdempotencyKeyLength = 89;

    public const int MaxDeliveryFailureCodeLength = 64;

    public const int MaxDeliveryFailureMessageLength = 256;
}

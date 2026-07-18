using System;

namespace Dignite.Abp.Notifications;

/// <summary>The final producer-side delivery intent for one preference candidate.</summary>
public sealed class NotificationDeliveryPreferenceDecision
{
    public Guid UserId { get; }

    public string Channel { get; }

    public NotificationDeliveryIntent Intent { get; }

    public DateTime? NotBefore { get; }

    public string? ReasonCode { get; }

    private NotificationDeliveryPreferenceDecision(
        Guid userId,
        string channel,
        NotificationDeliveryIntent intent,
        DateTime? notBefore,
        string? reasonCode)
    {
        UserId = userId;
        Channel = NotificationDeliveryIdentity.NormalizeChannel(channel);
        Intent = intent;
        NotBefore = notBefore;
        ReasonCode = reasonCode;
    }

    public static NotificationDeliveryPreferenceDecision Deliver(Guid userId, string channel)
    {
        return new NotificationDeliveryPreferenceDecision(
            userId,
            channel,
            NotificationDeliveryIntent.Deliver,
            null,
            null);
    }

    public static NotificationDeliveryPreferenceDecision Suppress(Guid userId, string channel, string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A suppression reason code is required.", nameof(reasonCode));
        }

        return new NotificationDeliveryPreferenceDecision(
            userId,
            channel,
            NotificationDeliveryIntent.Suppress,
            null,
            reasonCode.Trim());
    }

    public static NotificationDeliveryPreferenceDecision Delay(
        Guid userId,
        string channel,
        DateTime notBefore,
        string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A delay reason code is required.", nameof(reasonCode));
        }

        return new NotificationDeliveryPreferenceDecision(
            userId,
            channel,
            NotificationDeliveryIntent.Delay,
            notBefore,
            reasonCode.Trim());
    }
}

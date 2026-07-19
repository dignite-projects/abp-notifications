using System;

namespace Dignite.Abp.Notifications;

/// <summary>The producer-side delivery decision for one preference candidate: deliver, or suppress with a reason.</summary>
public sealed class NotificationDeliveryPreferenceDecision
{
    public Guid UserId { get; }

    public string Channel { get; }

    public bool ShouldDeliver { get; }

    public string? SuppressionReasonCode { get; }

    private NotificationDeliveryPreferenceDecision(
        Guid userId,
        string channel,
        bool shouldDeliver,
        string? suppressionReasonCode)
    {
        UserId = userId;
        Channel = NotificationDeliveryIdentity.NormalizeChannel(channel);
        ShouldDeliver = shouldDeliver;
        SuppressionReasonCode = suppressionReasonCode;
    }

    public static NotificationDeliveryPreferenceDecision Deliver(Guid userId, string channel)
    {
        return new NotificationDeliveryPreferenceDecision(userId, channel, true, null);
    }

    public static NotificationDeliveryPreferenceDecision Suppress(Guid userId, string channel, string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A suppression reason code is required.", nameof(reasonCode));
        }

        return new NotificationDeliveryPreferenceDecision(userId, channel, false, reasonCode.Trim());
    }
}

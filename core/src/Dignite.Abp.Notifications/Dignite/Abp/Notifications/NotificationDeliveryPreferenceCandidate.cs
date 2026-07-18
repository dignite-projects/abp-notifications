using System;

namespace Dignite.Abp.Notifications;

/// <summary>One already-eligible recipient/channel pair requiring a preference decision.</summary>
public sealed class NotificationDeliveryPreferenceCandidate
{
    public Guid UserId { get; }

    public string Channel { get; }

    public NotificationDeliveryPreferenceCandidate(Guid userId, string channel)
    {
        UserId = userId;
        _ = NotificationDeliveryIdentity.NormalizeChannel(channel);
        Channel = channel.Trim();
    }
}

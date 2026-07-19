using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Normalizes notification delivery channel names. Channel names are case-insensitive, matching
/// <see cref="NotificationChannels"/> routing.
/// </summary>
public static class NotificationDeliveryIdentity
{
    public const int MaxChannelNameLength = 128;

    public static string NormalizeChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("A delivery channel name is required.", nameof(channel));
        }

        var normalized = channel.Trim().ToUpperInvariant();
        if (normalized.Length > MaxChannelNameLength)
        {
            throw new ArgumentException(
                $"A delivery channel name cannot exceed {MaxChannelNameLength} characters.",
                nameof(channel));
        }

        return normalized;
    }
}

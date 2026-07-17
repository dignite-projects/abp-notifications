using System;
using System.Security.Cryptography;
using System.Text;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Creates the stable identity shared by scheduling, persistence and downstream channel providers.
/// Channel names are case-insensitive, matching <see cref="NotificationChannels"/> routing.
/// </summary>
public static class NotificationDeliveryIdentity
{
    public const int MaxChannelNameLength = 128;

    public const string IdempotencyKeyPrefix = "notification-delivery-v1:";

    public static Guid CreateId(Guid? tenantId, Guid notificationId, Guid userId, string channel)
    {
        var hash = ComputeHash(tenantId, notificationId, userId, channel);
        var idBytes = new byte[16];
        Array.Copy(hash, idBytes, idBytes.Length);
        return new Guid(idBytes);
    }

    public static string CreateIdempotencyKey(
        Guid? tenantId,
        Guid notificationId,
        Guid userId,
        string channel)
    {
        var hash = ComputeHash(tenantId, notificationId, userId, channel);
        var builder = new StringBuilder(IdempotencyKeyPrefix.Length + hash.Length * 2);
        builder.Append(IdempotencyKeyPrefix);
        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

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

    private static byte[] ComputeHash(Guid? tenantId, Guid notificationId, Guid userId, string channel)
    {
        var channelKey = NormalizeChannel(channel);
        var canonical = string.Concat(
            tenantId?.ToString("N") ?? "host",
            "|",
            notificationId.ToString("N"),
            "|",
            userId.ToString("N"),
            "|",
            channelKey.Length.ToString(),
            ":",
            channelKey);

        using (var sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        }
    }
}

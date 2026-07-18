using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dignite.Abp.Notifications;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Provider-neutral identities for nullable notification/channel preference scopes.</summary>
public static class NotificationDeliveryPreferenceIdentity
{
    public static Guid GetTenantKey(Guid? tenantId)
    {
        return NotificationSubscriptionIdentity.GetTenantKey(tenantId);
    }

    public static string GetNotificationNameKey(string? notificationName)
    {
        return notificationName == null
            ? ComputeHash("notification:any")
            : ComputeHash("notification", CheckValue(
                notificationName,
                nameof(notificationName),
                NotificationCenterConsts.MaxNotificationNameLength));
    }

    public static string GetChannelKey(string? channel)
    {
        return channel == null
            ? ComputeHash("channel:any")
            : ComputeHash("channel", NotificationDeliveryIdentity.NormalizeChannel(channel));
    }

    public static Guid CreatePreferenceId(
        Guid? tenantId,
        Guid userId,
        string? notificationName,
        string? channel)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A preference user identifier cannot be Guid.Empty.", nameof(userId));
        }

        return CreateGuid(
            "preference",
            GetTenantKey(tenantId).ToString("N", CultureInfo.InvariantCulture),
            userId.ToString("N", CultureInfo.InvariantCulture),
            GetNotificationNameKey(notificationName),
            GetChannelKey(channel));
    }

    public static Guid CreateQuietHoursId(Guid? tenantId, Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A quiet-hours user identifier cannot be Guid.Empty.", nameof(userId));
        }

        return CreateGuid(
            "quiet-hours",
            GetTenantKey(tenantId).ToString("N", CultureInfo.InvariantCulture),
            userId.ToString("N", CultureInfo.InvariantCulture));
    }

    private static string CheckValue(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be null, empty, or whitespace.", parameterName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot be longer than {maxLength} characters.", parameterName);
        }

        return value.Trim();
    }

    private static Guid CreateGuid(params string[] parts)
    {
        var hash = ComputeHashBytes(parts);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static string ComputeHash(params string[] parts)
    {
        var bytes = ComputeHashBytes(parts);
        var result = new StringBuilder(NotificationCenterConsts.DeliveryPreferenceIdentityKeyLength);
        foreach (var value in bytes)
        {
            result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return result.ToString();
    }

    private static byte[] ComputeHashBytes(params string[] parts)
    {
        var canonical = new StringBuilder();
        foreach (var part in parts)
        {
            canonical.Append(part.Length.ToString(CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(part);
        }

        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
    }
}

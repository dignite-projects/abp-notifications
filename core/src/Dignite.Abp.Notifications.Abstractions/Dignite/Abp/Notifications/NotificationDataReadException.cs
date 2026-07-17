using System;
using System.Text.Json;

namespace Dignite.Abp.Notifications;

/// <summary>Strict-read failure with a stable category callers can distinguish without parsing a message.</summary>
public class NotificationDataReadException : JsonException
{
    public UnsupportedNotificationDataReason Reason { get; }

    public string? Discriminator { get; }

    public int? SchemaVersion { get; }

    public NotificationDataReadException(
        UnsupportedNotificationDataReason reason,
        string message,
        string? discriminator = null,
        int? schemaVersion = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        Discriminator = discriminator;
        SchemaVersion = schemaVersion;
    }
}

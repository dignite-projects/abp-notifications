using System;

namespace Dignite.Abp.Notifications;

/// <summary>A channel result. Exceptions represent failures; suppression is an intentional terminal outcome.</summary>
public sealed class NotificationDeliveryResult
{
    public const int MaxReasonCodeLength = 64;

    public bool IsSuppressed { get; }

    public string? ReasonCode { get; }

    private NotificationDeliveryResult(bool isSuppressed, string? reasonCode)
    {
        IsSuppressed = isSuppressed;
        ReasonCode = reasonCode;
    }

    public static NotificationDeliveryResult Succeeded() => new NotificationDeliveryResult(false, null);

    public static NotificationDeliveryResult Suppressed(string reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("A suppression reason code is required.", nameof(reasonCode));
        }

        var normalized = reasonCode.Trim();
        if (normalized.Length > MaxReasonCodeLength)
        {
            throw new ArgumentException(
                $"A suppression reason code cannot exceed {MaxReasonCodeLength} characters.",
                nameof(reasonCode));
        }

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
            {
                throw new ArgumentException(
                    "A suppression reason code may contain only letters, numbers, '-', '_' and '.'.",
                    nameof(reasonCode));
            }
        }

        return new NotificationDeliveryResult(true, normalized);
    }
}

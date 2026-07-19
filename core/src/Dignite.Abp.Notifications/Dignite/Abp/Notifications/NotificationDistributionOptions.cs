using System;

namespace Dignite.Abp.Notifications;

/// <summary>Controls bounded recipient resolution, inbox persistence, and delivery-work scheduling.</summary>
public class NotificationDistributionOptions
{
    /// <summary>Hard safeguard for every distribution batch size.</summary>
    public const int MaxBatchSize = 10_000;

    /// <summary>Maximum explicit recipients distributed inline instead of through background jobs.</summary>
    public int DirectDistributionUserThreshold { get; set; } = 5;

    /// <summary>Maximum distinct recipient candidates resolved and evaluated at once.</summary>
    public int RecipientBatchSize { get; set; } = 256;

    /// <summary>Maximum inbox rows passed to one store batch write.</summary>
    public int UserNotificationWriteBatchSize { get; set; } = 256;

    /// <summary>
    /// Maximum single-recipient/channel work items scheduled by one operation. Each
    /// <see cref="NotificationDeliveryRequestedEto"/> still contains exactly one recipient and channel.
    /// </summary>
    public int DeliveryWorkItemBatchSize { get; set; } = 100;

    internal void Validate()
    {
        if (DirectDistributionUserThreshold < 0 || DirectDistributionUserThreshold > MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationDistributionOptions)}.{nameof(DirectDistributionUserThreshold)} must be " +
                $"between 0 and {MaxBatchSize}.");
        }

        ValidateBatchSize(RecipientBatchSize, nameof(RecipientBatchSize));
        ValidateBatchSize(UserNotificationWriteBatchSize, nameof(UserNotificationWriteBatchSize));
        ValidateBatchSize(DeliveryWorkItemBatchSize, nameof(DeliveryWorkItemBatchSize));
    }

    private static void ValidateBatchSize(int value, string propertyName)
    {
        if (value < 1 || value > MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationDistributionOptions)}.{propertyName} must be between 1 and {MaxBatchSize}.");
        }
    }
}

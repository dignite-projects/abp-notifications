using System;
using Volo.Abp.Collections;

namespace Dignite.Abp.Notifications;

public class NotificationOptions
{
    /// <summary>Hard safeguard for every configurable distribution batch size.</summary>
    public const int MaxDistributionBatchSize = 10_000;

    /// <summary>
    /// Maximum number of explicitly-targeted users a notification may have before it is distributed on a
    /// background job instead of inline. This was a hard-coded constant in the reference implementation;
    /// it is now configurable.
    /// </summary>
    public int DirectDistributionUserThreshold { get; set; } = 5;

    /// <summary>
    /// Maximum distinct candidates resolved and evaluated at once. Default 256 keeps policy work bounded and
    /// aligns with the persistence batch without creating large transient collections.
    /// </summary>
    public int RecipientBatchSize { get; set; } = 256;

    /// <summary>
    /// Maximum inbox rows passed to one store batch write. Default 256 remains below common relational
    /// parameter limits while giving EF Core and MongoDB an efficient multi-insert unit.
    /// </summary>
    public int UserNotificationWriteBatchSize { get; set; } = 256;

    /// <summary>
    /// Maximum recipients converted to per-recipient/channel <see cref="NotificationDeliveryWorkEto"/> records in
    /// one scheduling operation. Each published work item still contains exactly one recipient and channel. The
    /// existing property name is retained for compatibility with hosts that configured the bounded pipeline.
    /// </summary>
    public int DeliveryEventRecipientLimit { get; set; } = 100;

    /// <summary>How long a worker owns a delivery before another worker may recover it.</summary>
    public TimeSpan DeliveryLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum channel attempts, including abandoned leases.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Base delay before retrying the first failed attempt.</summary>
    public TimeSpan InitialDeliveryRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound for an automatically computed retry delay.</summary>
    public TimeSpan MaxDeliveryRetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Multiplier applied after each failed attempt.</summary>
    public double DeliveryRetryBackoffFactor { get; set; } = 2d;

    /// <summary>Random +/- fraction applied to the computed retry delay. Set to zero for deterministic scheduling.</summary>
    public double DeliveryRetryJitterFactor { get; set; } = 0.2d;

    /// <summary>Interval between scans for failed work and expired leases.</summary>
    public TimeSpan DeliveryRetryWorkerPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum work items republished by one retry scan.</summary>
    public int DeliveryRetryBatchSize { get; set; } = 100;

    /// <summary>Whether the process should scan and republish durable retry work.</summary>
    public bool IsDeliveryRetryWorkerEnabled { get; set; } = true;

    /// <summary>
    /// Gets definition provider types discovered across application modules or registered explicitly. Repeating the
    /// same provider type is idempotent; the definition manager executes each provider type once.
    /// </summary>
    public ITypeList<INotificationDefinitionProvider> DefinitionProviders { get; }

    public NotificationOptions()
    {
        DefinitionProviders = new TypeList<INotificationDefinitionProvider>();
    }

    internal void ValidateDistributionBatching()
    {
        if (DirectDistributionUserThreshold < 0 ||
            DirectDistributionUserThreshold > MaxDistributionBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(DirectDistributionUserThreshold)} must be between 0 and {MaxDistributionBatchSize}.");
        }

        ValidateBatchSize(RecipientBatchSize, nameof(RecipientBatchSize));
        ValidateBatchSize(UserNotificationWriteBatchSize, nameof(UserNotificationWriteBatchSize));
        ValidateBatchSize(DeliveryEventRecipientLimit, nameof(DeliveryEventRecipientLimit));
        ValidateBatchSize(DeliveryRetryBatchSize, nameof(DeliveryRetryBatchSize));

        if (DeliveryLeaseDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(DeliveryLeaseDuration)} must be greater than zero.");
        }

        if (MaxDeliveryAttempts < 1 || MaxDeliveryAttempts > 100)
        {
            throw new InvalidOperationException($"{nameof(MaxDeliveryAttempts)} must be between 1 and 100.");
        }

        if (InitialDeliveryRetryDelay < TimeSpan.Zero
            || MaxDeliveryRetryDelay < InitialDeliveryRetryDelay)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxDeliveryRetryDelay)} must be greater than or equal to {nameof(InitialDeliveryRetryDelay)}.");
        }

        if (!double.IsFinite(DeliveryRetryBackoffFactor) || DeliveryRetryBackoffFactor < 1d)
        {
            throw new InvalidOperationException($"{nameof(DeliveryRetryBackoffFactor)} must be finite and at least 1.");
        }

        if (!double.IsFinite(DeliveryRetryJitterFactor)
            || DeliveryRetryJitterFactor < 0d
            || DeliveryRetryJitterFactor > 1d)
        {
            throw new InvalidOperationException($"{nameof(DeliveryRetryJitterFactor)} must be finite and between 0 and 1.");
        }

        if (DeliveryRetryWorkerPeriod <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(DeliveryRetryWorkerPeriod)} must be greater than zero.");
        }
    }

    private static void ValidateBatchSize(int value, string propertyName)
    {
        if (value < 1 || value > MaxDistributionBatchSize)
        {
            throw new InvalidOperationException(
                $"{propertyName} must be between 1 and {MaxDistributionBatchSize}.");
        }
    }
}

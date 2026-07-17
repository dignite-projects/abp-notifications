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
    /// Maximum recipient IDs carried by one <see cref="NotificationDeliveryEto"/>. Default 100 bounds broker
    /// message growth independently of database and eligibility batch sizes.
    /// </summary>
    public int DeliveryEventRecipientLimit { get; set; } = 100;

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

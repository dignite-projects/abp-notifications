using System;

namespace Dignite.Abp.Notifications;

/// <summary>Controls bounded audience-source paging for tenant and host broadcasts.</summary>
public class NotificationAudienceBroadcastOptions
{
    /// <summary>Hard safeguard for an audience recipient page.</summary>
    public const int MaxBatchSize = 10_000;

    /// <summary>Maximum recipients read from an audience source in one page.</summary>
    public int RecipientBatchSize { get; set; } = 256;

    internal void Validate()
    {
        if (RecipientBatchSize < 1 || RecipientBatchSize > MaxBatchSize)
        {
            throw new InvalidOperationException(
                $"{nameof(NotificationAudienceBroadcastOptions)}.{nameof(RecipientBatchSize)} must be between 1 " +
                $"and {MaxBatchSize}.");
        }
    }
}

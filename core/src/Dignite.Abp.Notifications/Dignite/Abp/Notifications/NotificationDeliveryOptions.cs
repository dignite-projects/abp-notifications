using System;

namespace Dignite.Abp.Notifications;

/// <summary>Controls delivery leases, retry scheduling, and retry-worker configuration.</summary>
public class NotificationDeliveryOptions
{
    /// <summary>Hard safeguard for retry-worker batch sizes.</summary>
    public const int MaxBatchSize = 10_000;

    /// <summary>Duration for which a claimed delivery attempt remains exclusively leased.</summary>
    public TimeSpan DeliveryLeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum delivery attempts before a notification is marked permanently failed.</summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>Delay before the first retry of a transient delivery failure.</summary>
    public TimeSpan InitialDeliveryRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Upper bound applied to exponential retry delays.</summary>
    public TimeSpan MaxDeliveryRetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Multiplier applied between consecutive retry delays.</summary>
    public double DeliveryRetryBackoffFactor { get; set; } = 2d;

    /// <summary>Maximum proportional random jitter applied to retry delays.</summary>
    public double DeliveryRetryJitterFactor { get; set; } = 0.2d;

    /// <summary>Period between retry-worker scans.</summary>
    public TimeSpan DeliveryRetryWorkerPeriod { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum due delivery attempts claimed by one retry-worker scan.</summary>
    public int DeliveryRetryBatchSize { get; set; } = 100;

    /// <summary>Whether the built-in delivery retry worker is enabled.</summary>
    public bool IsDeliveryRetryWorkerEnabled { get; set; } = true;

    internal void Validate()
    {
        if (DeliveryLeaseDuration <= TimeSpan.Zero)
        {
            throw Invalid(nameof(DeliveryLeaseDuration), "must be greater than zero");
        }

        if (MaxDeliveryAttempts < 1 || MaxDeliveryAttempts > 100)
        {
            throw Invalid(nameof(MaxDeliveryAttempts), "must be between 1 and 100");
        }

        if (InitialDeliveryRetryDelay < TimeSpan.Zero || MaxDeliveryRetryDelay < InitialDeliveryRetryDelay)
        {
            throw Invalid(
                nameof(MaxDeliveryRetryDelay),
                $"must be greater than or equal to " +
                $"{nameof(NotificationDeliveryOptions)}.{nameof(InitialDeliveryRetryDelay)}");
        }

        if (!double.IsFinite(DeliveryRetryBackoffFactor) || DeliveryRetryBackoffFactor < 1d)
        {
            throw Invalid(nameof(DeliveryRetryBackoffFactor), "must be finite and at least 1");
        }

        if (!double.IsFinite(DeliveryRetryJitterFactor)
            || DeliveryRetryJitterFactor < 0d
            || DeliveryRetryJitterFactor > 1d)
        {
            throw Invalid(nameof(DeliveryRetryJitterFactor), "must be finite and between 0 and 1");
        }

        if (DeliveryRetryWorkerPeriod <= TimeSpan.Zero)
        {
            throw Invalid(nameof(DeliveryRetryWorkerPeriod), "must be greater than zero");
        }

        if (DeliveryRetryBatchSize < 1 || DeliveryRetryBatchSize > MaxBatchSize)
        {
            throw Invalid(nameof(DeliveryRetryBatchSize), $"must be between 1 and {MaxBatchSize}");
        }
    }

    private static InvalidOperationException Invalid(string propertyName, string requirement)
    {
        return new InvalidOperationException(
            $"{nameof(NotificationDeliveryOptions)}.{propertyName} {requirement}.");
    }
}

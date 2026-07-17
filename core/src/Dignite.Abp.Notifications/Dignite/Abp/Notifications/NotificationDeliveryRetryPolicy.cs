using System;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

public class NotificationDeliveryRetryPolicy : INotificationDeliveryRetryPolicy, ITransientDependency
{
    protected NotificationOptions Options { get; }

    public NotificationDeliveryRetryPolicy(IOptions<NotificationOptions> options)
    {
        Options = options.Value;
    }

    public virtual DateTime? GetNextAttemptTime(DateTime failedAt, int attemptCount)
    {
        if (attemptCount >= Options.MaxDeliveryAttempts)
        {
            return null;
        }

        var multiplier = Math.Pow(Options.DeliveryRetryBackoffFactor, Math.Max(0, attemptCount - 1));
        var delayMilliseconds = Math.Min(
            Options.MaxDeliveryRetryDelay.TotalMilliseconds,
            Options.InitialDeliveryRetryDelay.TotalMilliseconds * multiplier);
        if (Options.DeliveryRetryJitterFactor > 0)
        {
            var jitter = (Random.Shared.NextDouble() * 2d - 1d) * Options.DeliveryRetryJitterFactor;
            delayMilliseconds = Math.Max(0d, delayMilliseconds * (1d + jitter));
        }

        return failedAt.AddMilliseconds(delayMilliseconds);
    }
}

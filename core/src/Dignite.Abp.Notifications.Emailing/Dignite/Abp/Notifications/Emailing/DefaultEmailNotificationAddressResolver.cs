using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Runs the <see cref="IEmailNotificationAddressProvider"/> chain and flattens its three-state result down to the
/// single question the notifier asks: send, or don't. Mirrors <see cref="DefaultNotificationEmailBuilder"/>.
/// </summary>
/// <remarks>
/// Transient, never a singleton: providers may hold repositories, and a singleton capturing them is exactly the
/// lifetime bug in <c>notifications-invariants.md</c> §2.
/// </remarks>
public class DefaultEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ITransientDependency
{
    protected IReadOnlyList<IEmailNotificationAddressProvider> Providers { get; }

    public DefaultEmailNotificationAddressResolver(
        IEnumerable<IEmailNotificationAddressProvider> providers,
        ILogger<DefaultEmailNotificationAddressResolver> logger)
    {
        Providers = providers
            .OrderBy(provider => provider.Order)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToList();

        if (Providers.Count == 0)
        {
            // Resolved once per delivered event, not once per recipient — this notifier is transient.
            logger.LogWarning(
                "Notification emailing is installed but no {Provider} is registered, so no notification emails will "
                + "be sent. Install Dignite.Abp.Notifications.Emailing.Identity or register your own provider.",
                nameof(IEmailNotificationAddressProvider));
        }
    }

    public virtual async Task<string?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context)
    {
        foreach (var provider in Providers)
        {
            var address = await provider.GetAddressOrNullAsync(context);
            if (address != null)
            {
                // Claimed. EmailNotificationAddress.None means "do not email this user" and must stop the chain,
                // otherwise a later provider (e.g. the Identity fallback) would silently override an opt-out.
                return address.Address;
            }
        }

        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Relays notifications to email — the second notifier that stress-tests the framework's event contract. Unlike
/// SignalR (which addresses users directly), email needs a UserId → address mapping, supplied by the
/// <see cref="IEmailNotificationAddressResolver"/> chain. Idempotency across duplicate event delivery comes from the
/// transactional inbox (roadmap C/P1). Honors channel routing via <see cref="NotificationChannels"/>.
/// </summary>
public class EmailNotifier : INotificationNotifier<NotificationDeliveryEto>, ITransientDependency
{
    public const string ChannelName = "Email";

    public string Name => ChannelName;

    protected IEmailSender EmailSender { get; }

    protected IReadOnlyList<IEmailNotificationAddressResolver> AddressResolvers { get; }

    protected INotificationEmailBuilder EmailBuilder { get; }

    protected ILogger<EmailNotifier> Logger { get; }

    public EmailNotifier(
        IEmailSender emailSender,
        IEnumerable<IEmailNotificationAddressResolver> addressResolvers,
        INotificationEmailBuilder emailBuilder,
        ILogger<EmailNotifier> logger)
    {
        EmailSender = emailSender;
        EmailBuilder = emailBuilder;
        Logger = logger;

        // Same Order-then-FullName-Ordinal tiebreak DefaultNotificationEmailBuilder uses for content providers, so
        // which address a user receives never depends on DI registration order.
        AddressResolvers = addressResolvers
            .OrderBy(resolver => resolver.Order)
            .ThenBy(resolver => resolver.GetType().FullName, StringComparer.Ordinal)
            .ToList();

        if (AddressResolvers.Count == 0)
        {
            // This notifier is transient, so the chain is inspected once per delivered event rather than per recipient.
            Logger.LogWarning(
                "Notification emailing is installed but no {Resolver} is registered, so no notification emails will "
                + "be sent. Install Dignite.Abp.Notifications.Emailing.Identity or register your own resolver.",
                nameof(IEmailNotificationAddressResolver));
        }
    }

    public virtual async Task HandleEventAsync(NotificationDeliveryEto eventData)
    {
        if (!NotificationChannels.IsAllowed(eventData.Channels, Name)
            || eventData.UserIds == null
            || eventData.UserIds.Length == 0)
        {
            return;
        }

        var notification = NotificationDelivery.FromEto(eventData);

        // One email per recipient, even when two recipients resolve to the same mailbox: ASP.NET Core Identity's
        // UserOptions.RequireUniqueEmail defaults to false, so a shared account is legitimate, and the body is built
        // per recipient. Deduplicating by address would trade N loud duplicates for a silently dropped email.
        foreach (var userId in eventData.UserIds.Distinct())
        {
            var context = new EmailNotificationAddressResolveContext(notification, userId, eventData.TenantId);
            var address = await ResolveAddressOrNullAsync(context);
            if (string.IsNullOrWhiteSpace(address))
            {
                continue;
            }

            var email = await EmailBuilder.BuildAsync(
                new NotificationEmailBuildContext(notification, userId, address!, eventData.TenantId));
            if (email == null)
            {
                Logger.LogDebug(
                    "No email content provider produced content for notification '{NotificationName}' and user '{UserId}'.",
                    eventData.NotificationName,
                    userId);
                continue;
            }

            await EmailSender.SendAsync(address!, email.Subject, email.Body, email.IsBodyHtml);
        }
    }

    /// <summary>Walks the resolver chain and takes the first non-null address.</summary>
    protected virtual async Task<string?> ResolveAddressOrNullAsync(EmailNotificationAddressResolveContext context)
    {
        foreach (var resolver in AddressResolvers)
        {
            var address = await resolver.GetEmailOrNullAsync(context);
            if (!string.IsNullOrWhiteSpace(address))
            {
                return address;
            }
        }

        return null;
    }
}

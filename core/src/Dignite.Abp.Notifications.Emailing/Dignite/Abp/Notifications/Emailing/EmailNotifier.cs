using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Relays notifications to email — the second notifier that stress-tests the framework's event contract. Unlike
/// SignalR (which addresses users directly), email needs a UserId → address mapping, supplied by the app-provided
/// <see cref="IEmailNotificationAddressResolver"/>. Idempotency across duplicate event delivery comes from the
/// transactional inbox (roadmap C/P1). Honors channel routing via <see cref="NotificationChannels"/>.
/// </summary>
public class EmailNotifier : INotificationNotifier<RealTimeNotifyEto>, ITransientDependency
{
    public const string ChannelName = "Email";

    public string Name => ChannelName;

    protected IEmailSender EmailSender { get; }

    protected IEmailNotificationAddressResolver AddressResolver { get; }

    protected INotificationEmailBuilder EmailBuilder { get; }

    public EmailNotifier(
        IEmailSender emailSender,
        IEmailNotificationAddressResolver addressResolver,
        INotificationEmailBuilder emailBuilder)
    {
        EmailSender = emailSender;
        AddressResolver = addressResolver;
        EmailBuilder = emailBuilder;
    }

    public virtual async Task HandleEventAsync(RealTimeNotifyEto eventData)
    {
        if (!NotificationChannels.IsAllowed(eventData.Channels, Name)
            || eventData.UserIds == null
            || eventData.UserIds.Length == 0)
        {
            return;
        }

        var email = await EmailBuilder.BuildAsync(RealTimeNotification.FromEto(eventData));

        foreach (var userId in eventData.UserIds.Distinct())
        {
            var address = await AddressResolver.GetEmailOrNullAsync(userId);
            if (!string.IsNullOrWhiteSpace(address))
            {
                await EmailSender.SendAsync(address!, email.Subject, email.Body, email.IsBodyHtml);
            }
        }
    }
}

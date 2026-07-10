using System.Threading.Tasks;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Resolves the email address for one recipient of one notification by running the
/// <see cref="IEmailNotificationAddressProvider"/> chain. This is the aggregate <c>EmailNotifier</c> depends on; an
/// app extends the pipeline by registering a provider, not by replacing this.
/// </summary>
/// <remarks>
/// Mirrors the content pipeline: <see cref="INotificationEmailBuilder"/> is to
/// <see cref="INotificationEmailContentProvider"/> what this is to <see cref="IEmailNotificationAddressProvider"/>.
/// Replace the default implementation only to change the selection <i>policy</i> (ordering, first-match); to change
/// where an address comes from, add a provider.
/// </remarks>
public interface IEmailNotificationAddressResolver
{
    /// <summary>The address to send to, or null when this user should not be emailed.</summary>
    Task<string?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context);
}

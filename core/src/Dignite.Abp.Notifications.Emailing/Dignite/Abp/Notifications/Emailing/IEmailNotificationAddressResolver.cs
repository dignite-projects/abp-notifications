using System.Threading.Tasks;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Resolves a user's email address for the Email notifier. Apps supply a real implementation
/// (for example, backed by ABP Identity or a remote user service).
/// </summary>
public interface IEmailNotificationAddressResolver
{
    Task<string?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context);
}

using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Resolves a user's email address for the Email notifier. This is the optional, shared "UserId → endpoint"
/// mapping the roadmap calls for — apps supply a real implementation (e.g. backed by ABP Identity).
/// </summary>
public interface IEmailNotificationAddressResolver
{
    Task<string?> GetEmailOrNullAsync(Guid userId);
}

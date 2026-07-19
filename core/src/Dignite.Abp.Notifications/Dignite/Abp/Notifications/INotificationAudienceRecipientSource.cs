using System.Threading;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Resolves a named audience into one bounded, tenant-or-host scoped recipient page at a time.
/// </summary>
public interface INotificationAudienceRecipientSource
{
    /// <summary>
    /// The stable audience name served by this source.
    /// </summary>
    string AudienceName { get; }

    /// <summary>
    /// Gets the next recipient page. Implementations must not return more than
    /// <see cref="NotificationAudienceRecipientPageRequest.MaxResultCount"/> users.
    /// </summary>
    Task<NotificationAudienceRecipientPage> GetRecipientsAsync(
        NotificationAudienceRecipientPageRequest request,
        CancellationToken cancellationToken = default);
}

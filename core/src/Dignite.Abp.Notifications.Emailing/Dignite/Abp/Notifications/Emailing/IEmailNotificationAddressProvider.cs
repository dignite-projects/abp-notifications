using System.Threading.Tasks;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Supplies the email address for one recipient of one notification. Providers are chained in <see cref="Order"/>,
/// and the first one to claim the notification wins — the same shape as <see cref="INotificationEmailContentProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A provider returns the address <i>for this user</i> in this entity context — never "the entity's address".</b>
/// <see cref="EmailNotificationAddressResolveContext.UserId"/> is always one of the notification's recipients, and
/// <c>EmailNotifier</c> builds the body for that same user. A provider that keys off
/// <c>context.Notification.EntityId</c> alone returns one address for every recipient, so N recipients get N copies
/// of a body written for someone else, and the entity's contact may not be a recipient at all.
/// </para>
/// <para>
/// Claim on <c>context.Notification.NotificationName</c> in preference to <c>EntityTypeName</c>: one order entity can
/// carry both <c>"Mall.OrderShipped"</c> (the account email) and <c>"Mall.OrderContactChanged"</c> (the order's
/// contact). <c>EntityTypeName</c> / <c>EntityId</c> are for looking the entity <i>up</i>, not for deciding ownership.
/// </para>
/// <para>
/// <b>Tenancy</b>: never call <c>CurrentTenant.Change</c>. ABP's event bus has already entered the notification's
/// tenant before the notifier runs, so a repository-backed provider queries under the ambient tenant.
/// <see cref="EmailNotificationAddressResolveContext.TenantId"/> is for providers that must forward the tenant across
/// a boundary the ambient scope cannot cross, such as a remote user service.
/// </para>
/// </remarks>
public interface IEmailNotificationAddressProvider
{
    /// <summary>Lower runs first. See <see cref="EmailNotificationAddressProviderOrders"/>.</summary>
    int Order { get; }

    /// <summary>
    /// Returns null to pass to the next provider, <see cref="EmailNotificationAddress.None"/> to claim the
    /// notification and send nothing, or <see cref="EmailNotificationAddress.To"/> to claim it and supply an address.
    /// </summary>
    Task<EmailNotificationAddress?> GetAddressOrNullAsync(EmailNotificationAddressResolveContext context);
}

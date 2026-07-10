using Volo.Abp;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// The outcome of an <see cref="IEmailNotificationAddressProvider"/> that claimed a notification.
/// </summary>
/// <remarks>
/// Three states, not two. A provider returns <c>null</c> to mean "not mine, try the next one";
/// <see cref="None"/> to mean "mine, and this user must not be emailed"; and <see cref="To"/> to supply an address.
/// <para>
/// Without <see cref="None"/> a provider that means "this user opted out of email for this order" would return null,
/// fall through to the built-in Identity provider, and mail the account address anyway — silently bypassing the
/// opt-out. <c>INotificationEmailContentProvider</c> gets by with two states because falling through there produces a
/// worse body for the right recipient; falling through here sends mail to a different address.
/// </para>
/// </remarks>
public sealed class EmailNotificationAddress
{
    /// <summary>The address to send to, or null when this user must not be emailed.</summary>
    public string? Address { get; }

    private EmailNotificationAddress(string? address)
    {
        Address = address;
    }

    /// <summary>Claim the notification and suppress the email for this user.</summary>
    public static readonly EmailNotificationAddress None = new(null);

    /// <summary>Claim the notification and send to <paramref name="address"/>.</summary>
    public static EmailNotificationAddress To(string address)
    {
        return new EmailNotificationAddress(Check.NotNullOrWhiteSpace(address, nameof(address)));
    }
}

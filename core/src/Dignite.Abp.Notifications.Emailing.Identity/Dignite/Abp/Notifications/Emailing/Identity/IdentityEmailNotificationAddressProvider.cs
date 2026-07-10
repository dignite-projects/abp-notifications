using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Dignite.Abp.Notifications.Emailing.Identity;

/// <summary>
/// Built-in fallback provider: the recipient's ABP Identity account email.
/// </summary>
/// <remarks>
/// Sits at <see cref="EmailNotificationAddressProviderOrders.BuiltInFallback"/>, so any application provider at
/// <see cref="EmailNotificationAddressProviderOrders.Default"/> gets to claim a notification first. It does not
/// replace services — a business provider coexists with it rather than displacing it.
/// <para>
/// Queries Identity under the ambient tenant and never switches tenants itself. A notifier is an
/// <c>IDistributedEventHandler&lt;NotificationDeliveryEto&gt;</c>, and ABP's <c>EventBusBase</c> already enters
/// <c>NotificationDeliveryEto.TenantId</c> before invoking it — in-process and across a message broker alike.
/// Providers that reach a user directory outside this process (a remote user service) need the tenant on the wire
/// instead, which is why <see cref="EmailNotificationAddressResolveContext.TenantId"/> still carries it explicitly.
/// </para>
/// </remarks>
public class IdentityEmailNotificationAddressProvider : IEmailNotificationAddressProvider, ITransientDependency
{
    public int Order => EmailNotificationAddressProviderOrders.BuiltInFallback;

    protected IIdentityUserRepository UserRepository { get; }

    public IdentityEmailNotificationAddressProvider(IIdentityUserRepository userRepository)
    {
        UserRepository = userRepository;
    }

    public virtual async Task<EmailNotificationAddress?> GetAddressOrNullAsync(
        EmailNotificationAddressResolveContext context)
    {
        var user = await UserRepository.FindAsync(context.UserId);

        // Last in the chain, so "this user has no address" is a claim rather than a pass: there is nothing left to
        // fall through to, and returning null would only make the aggregate walk an empty tail.
        return string.IsNullOrWhiteSpace(user?.Email)
            ? EmailNotificationAddress.None
            : EmailNotificationAddress.To(user!.Email);
    }
}

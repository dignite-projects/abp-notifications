using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Dignite.Abp.Notifications.Emailing.Identity;

/// <summary>
/// Built-in fallback resolver: the recipient's ABP Identity account email.
/// </summary>
/// <remarks>
/// Sits at <see cref="NotificationEmailProviderOrders.BuiltInFallback"/>, so any application resolver at
/// <see cref="NotificationEmailProviderOrders.Default"/> gets to claim a notification first. It does not replace
/// services — an application resolver coexists with it rather than displacing it.
/// <para>
/// Queries Identity under the ambient tenant and never switches tenants itself. A notifier is an
/// <c>IDistributedEventHandler&lt;NotificationDeliveryEto&gt;</c>, and ABP's <c>EventBusBase</c> already enters
/// <c>NotificationDeliveryEto.TenantId</c> before invoking it — in-process and across a message broker alike.
/// Resolvers that reach a user directory outside this process (a remote user service) need the tenant on the wire
/// instead, which is why <see cref="EmailNotificationAddressResolveContext.TenantId"/> still carries it explicitly.
/// </para>
/// </remarks>
public class IdentityEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ITransientDependency
{
    public int Order => NotificationEmailProviderOrders.BuiltInFallback;

    protected IIdentityUserRepository UserRepository { get; }

    public IdentityEmailNotificationAddressResolver(IIdentityUserRepository userRepository)
    {
        UserRepository = userRepository;
    }

    public virtual async Task<string?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context)
    {
        var user = await UserRepository.FindAsync(context.UserId);
        return string.IsNullOrWhiteSpace(user?.Email) ? null : user.Email;
    }
}

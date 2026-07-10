using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Dignite.Abp.Notifications.Emailing.Identity;

/// <summary>
/// Resolves notification email recipients from ABP Identity users.
/// </summary>
/// <remarks>
/// Queries Identity under the ambient tenant and never switches tenants itself. A notifier is an
/// <c>IDistributedEventHandler&lt;NotificationDeliveryEto&gt;</c>, and ABP's <c>EventBusBase</c> already enters
/// <c>NotificationDeliveryEto.TenantId</c> before invoking it — in-process and across a message broker alike.
/// Resolvers that reach a user directory outside this process (a remote user service) need the tenant on the wire
/// instead, which is why <see cref="EmailNotificationAddressResolveContext.TenantId"/> still carries it explicitly.
/// </remarks>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IEmailNotificationAddressResolver))]
public class IdentityEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ITransientDependency
{
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

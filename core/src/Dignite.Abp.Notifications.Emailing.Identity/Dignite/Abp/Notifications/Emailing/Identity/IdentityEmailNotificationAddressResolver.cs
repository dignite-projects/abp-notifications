using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications.Emailing.Identity;

/// <summary>
/// Resolves notification email recipients from ABP Identity users.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IEmailNotificationAddressResolver))]
public class IdentityEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ITransientDependency
{
    protected IIdentityUserRepository UserRepository { get; }

    protected ICurrentTenant CurrentTenant { get; }

    public IdentityEmailNotificationAddressResolver(
        IIdentityUserRepository userRepository,
        ICurrentTenant currentTenant)
    {
        UserRepository = userRepository;
        CurrentTenant = currentTenant;
    }

    public virtual async Task<string?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context)
    {
        using (CurrentTenant.Change(context.TenantId, null))
        {
            var user = await UserRepository.FindAsync(context.UserId);
            return string.IsNullOrWhiteSpace(user?.Email) ? null : user.Email;
        }
    }
}

using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace Dignite.Abp.Notifications.Emailing.Identity;

/// <summary>
/// Resolves notification email recipients from ABP Identity users.
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IEmailNotificationAddressResolver))]
public class IdentityEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ITransientDependency
{
    protected IIdentityUserRepository UserRepository { get; }

    public IdentityEmailNotificationAddressResolver(IIdentityUserRepository userRepository)
    {
        UserRepository = userRepository;
    }

    public virtual async Task<string?> GetEmailOrNullAsync(Guid userId)
    {
        var user = await UserRepository.FindAsync(userId);
        return string.IsNullOrWhiteSpace(user?.Email) ? null : user.Email;
    }
}


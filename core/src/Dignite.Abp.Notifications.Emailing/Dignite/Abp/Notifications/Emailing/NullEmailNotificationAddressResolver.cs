using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

/// <summary>Default resolver: no address is known, so nothing is emailed until an app provides a real resolver.</summary>
public class NullEmailNotificationAddressResolver : IEmailNotificationAddressResolver, ISingletonDependency
{
    public Task<string?> GetEmailOrNullAsync(Guid userId)
    {
        return Task.FromResult<string?>(null);
    }
}

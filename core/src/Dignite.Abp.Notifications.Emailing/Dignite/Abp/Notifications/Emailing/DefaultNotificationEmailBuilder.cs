using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

public class DefaultNotificationEmailBuilder : INotificationEmailBuilder, ITransientDependency
{
    protected IReadOnlyList<INotificationEmailContentProvider> ContentProviders { get; }

    public DefaultNotificationEmailBuilder(IEnumerable<INotificationEmailContentProvider> contentProviders)
    {
        ContentProviders = contentProviders
            .OrderBy(provider => provider.Order)
            .ThenBy(provider => provider.GetType().FullName, StringComparer.Ordinal)
            .ToList();
    }

    public virtual async Task<NotificationEmail?> BuildAsync(NotificationEmailBuildContext context)
    {
        foreach (var provider in ContentProviders)
        {
            var email = await provider.BuildOrNullAsync(context);
            if (email != null)
            {
                return email;
            }
        }

        return null;
    }
}

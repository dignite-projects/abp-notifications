using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

public class LocalizableMessageNotificationEmailContentProvider : INotificationEmailContentProvider, ITransientDependency
{
    protected IStringLocalizerFactory StringLocalizerFactory { get; }

    public int Order => NotificationEmailContentProviderOrders.BuiltInFallback;

    public LocalizableMessageNotificationEmailContentProvider(IStringLocalizerFactory stringLocalizerFactory)
    {
        StringLocalizerFactory = stringLocalizerFactory;
    }

    public virtual Task<NotificationEmail?> BuildOrNullAsync(NotificationEmailBuildContext context)
    {
        if (context.Notification.Data is not LocalizableMessageNotificationData data)
        {
            return Task.FromResult<NotificationEmail?>(null);
        }

        var localizer = data.ResourceName != null
            ? StringLocalizerFactory.CreateByResourceNameOrNull(data.ResourceName)
            : null;
        localizer ??= StringLocalizerFactory.CreateDefaultOrNull();

        var body = localizer == null
            ? data.Name
            : data.Arguments != null
                ? localizer[data.Name, data.Arguments.Values.ToArray()].Value
                : localizer[data.Name].Value;

        return Task.FromResult<NotificationEmail?>(
            new NotificationEmail(context.Notification.NotificationName, body));
    }
}

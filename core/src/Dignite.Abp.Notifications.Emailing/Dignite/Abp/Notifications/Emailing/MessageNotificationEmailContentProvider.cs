using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

public class MessageNotificationEmailContentProvider : INotificationEmailContentProvider, ITransientDependency
{
    public int Order => NotificationEmailContentProviderOrders.BuiltInFallback;

    public virtual Task<NotificationEmail?> BuildOrNullAsync(NotificationEmailBuildContext context)
    {
        if (context.Notification.Data is not MessageNotificationData message)
        {
            return Task.FromResult<NotificationEmail?>(null);
        }

        return Task.FromResult<NotificationEmail?>(
            new NotificationEmail(context.Notification.NotificationName, message.Message));
    }
}

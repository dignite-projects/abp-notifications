using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications.Emailing;

public class DefaultNotificationEmailBuilder : INotificationEmailBuilder, ITransientDependency
{
    public virtual Task<NotificationEmail> BuildAsync(NotificationDelivery notification)
    {
        var body = notification.Data is MessageNotificationData message
            ? message.Message
            : "You have a new notification.";

        return Task.FromResult(new NotificationEmail(notification.NotificationName, body));
    }
}

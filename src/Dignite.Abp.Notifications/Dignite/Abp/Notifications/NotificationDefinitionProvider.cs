namespace Dignite.Abp.Notifications;

public abstract class NotificationDefinitionProvider : INotificationDefinitionProvider
{
    public abstract void Define(INotificationDefinitionContext context);
}

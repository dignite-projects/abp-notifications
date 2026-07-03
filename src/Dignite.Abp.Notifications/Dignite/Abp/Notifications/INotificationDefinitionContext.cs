namespace Dignite.Abp.Notifications;

public interface INotificationDefinitionContext
{
    NotificationDefinition Add(NotificationDefinition definition);

    NotificationDefinition? GetOrNull(string name);
}

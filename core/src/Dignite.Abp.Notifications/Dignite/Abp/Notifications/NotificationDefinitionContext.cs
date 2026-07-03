using System.Collections.Generic;

namespace Dignite.Abp.Notifications;

public class NotificationDefinitionContext : INotificationDefinitionContext
{
    internal Dictionary<string, NotificationDefinition> Definitions { get; }

    public NotificationDefinitionContext()
    {
        Definitions = new Dictionary<string, NotificationDefinition>();
    }

    public NotificationDefinition Add(NotificationDefinition definition)
    {
        Definitions[definition.Name] = definition;
        return definition;
    }

    public NotificationDefinition? GetOrNull(string name)
    {
        return Definitions.TryGetValue(name, out var definition) ? definition : null;
    }
}

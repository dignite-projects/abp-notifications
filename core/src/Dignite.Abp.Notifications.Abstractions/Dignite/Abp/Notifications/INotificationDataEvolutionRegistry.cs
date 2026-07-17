using System.Text.Json.Nodes;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Optional evolution capabilities implemented by the built-in type registry. Custom type registries that do not
/// implement this interface retain legacy schema-v1 behavior.
/// </summary>
public interface INotificationDataEvolutionRegistry
{
    int GetCurrentSchemaVersion(string discriminator);

    /// <summary>Runs every registered consecutive step from <paramref name="fromVersion"/> to current.</summary>
    JsonObject Upcast(string discriminator, int fromVersion, JsonObject payload);
}

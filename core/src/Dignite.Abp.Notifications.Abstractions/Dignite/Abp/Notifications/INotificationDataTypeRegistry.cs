using System;
using System.Text.Json.Nodes;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Single source of truth mapping stable discriminators to <see cref="NotificationData"/> CLR types.
/// Used by serialization on both the writing (server) and reading (remote client) sides. Discriminator
/// registration and lookup both use ordinal, case-sensitive comparison.
/// </summary>
public interface INotificationDataTypeRegistry
{
    string? GetDiscriminatorOrNull(Type dataType);

    Type? GetTypeOrNull(string discriminator);

    /// <summary>Gets the registered current schema version for a stable discriminator.</summary>
    int GetCurrentSchemaVersion(string discriminator);

    /// <summary>Runs every registered consecutive step from <paramref name="fromVersion"/> to current.</summary>
    JsonObject Upcast(string discriminator, int fromVersion, JsonObject payload);
}

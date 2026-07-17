using System;
using System.Reflection;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Declares a stable, version-independent discriminator for a <see cref="NotificationData"/> type.
/// This name — not the CLR type name / AssemblyQualifiedName — is what gets persisted and sent over the wire,
/// so it must stay constant across assembly versions and refactors. The optional second constructor argument is
/// the current JSON schema version; existing one-argument declarations remain schema v1.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class NotificationDataTypeAttribute : Attribute
{
    public string Name { get; }

    /// <summary>The current schema version written for this payload type.</summary>
    public int SchemaVersion { get; }

    public NotificationDataTypeAttribute(string name)
        : this(name, NotificationDataSchema.LegacyVersion)
    {
    }

    public NotificationDataTypeAttribute(string name, int schemaVersion)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name));
        if (schemaVersion < NotificationDataSchema.LegacyVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                $"Schema versions start at {NotificationDataSchema.LegacyVersion}.");
        }

        SchemaVersion = schemaVersion;
    }

    public static string? GetNameOrNull(Type type)
    {
        return type.GetCustomAttribute<NotificationDataTypeAttribute>(inherit: false)?.Name;
    }

    public static int GetSchemaVersionOrDefault(Type type)
    {
        return type.GetCustomAttribute<NotificationDataTypeAttribute>(inherit: false)?.SchemaVersion
            ?? NotificationDataSchema.LegacyVersion;
    }
}

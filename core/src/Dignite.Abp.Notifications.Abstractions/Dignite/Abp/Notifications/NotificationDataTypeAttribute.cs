using System;
using System.Reflection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Declares a stable, version-independent discriminator for a <see cref="NotificationData"/> type.
/// This name — not the CLR type name / AssemblyQualifiedName — is what gets persisted and sent over the wire,
/// so it must stay constant across assembly versions and refactors.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class NotificationDataTypeAttribute : Attribute
{
    public string Name { get; }

    public NotificationDataTypeAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Notification data type discriminator cannot be null or whitespace.", nameof(name));
        }

        Name = name;
    }

    public static string? GetNameOrNull(Type type)
    {
        return type.GetCustomAttribute<NotificationDataTypeAttribute>(inherit: false)?.Name;
    }
}

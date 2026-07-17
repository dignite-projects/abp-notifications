using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Localization;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A notification type registered by a business module: its stable name, display text, optional
/// permission/feature gating, opt-in stable payload/entity contracts, and free-form attributes.
/// </summary>
public class NotificationDefinition
{
    public string Name { get; }

    public Type? EntityType { get; }

    public ILocalizableString DisplayName { get; set; }

    public ILocalizableString? Description { get; set; }

    /// <summary>
    /// When set, a user must be granted this permission both to subscribe and to receive the notification.
    /// </summary>
    public string? PermissionName { get; private set; }

    /// <summary>
    /// When set, this feature must be enabled in the notification tenant both to subscribe and to receive it.
    /// </summary>
    public string? FeatureName { get; private set; }

    /// <summary>
    /// The stable discriminator required for a published payload, or <see langword="null"/> when this legacy
    /// definition has not opted into payload validation.
    /// </summary>
    public string? PayloadDiscriminator { get; private set; }

    /// <summary>
    /// Whether publish calls must omit, may include, or must include an entity identity. The default
    /// <see cref="NotificationEntityRequirement.Unspecified"/> preserves existing definition behavior.
    /// </summary>
    public NotificationEntityRequirement EntityRequirement { get; private set; }

    /// <summary>
    /// An optional stable entity type name enforced when an entity identity is supplied. This is never a CLR type name.
    /// </summary>
    public string? ExpectedEntityTypeName { get; private set; }

    internal Type? PayloadType { get; private set; }

    /// <summary>Free-form extension bag — e.g. explicit external channel routing.</summary>
    public IDictionary<string, object?> Attributes { get; }

    public NotificationDefinition(string name, ILocalizableString displayName, Type? entityType = null)
    {
        Name = Check.NotNull(name, nameof(name));
        DisplayName = Check.NotNull(displayName, nameof(displayName));
        EntityType = entityType;
        Attributes = new Dictionary<string, object?>();
    }

    public NotificationDefinition RequirePermission(string permissionName)
    {
        PermissionName = permissionName;
        return this;
    }

    public NotificationDefinition RequireFeature(string featureName)
    {
        FeatureName = featureName;
        return this;
    }

    /// <summary>
    /// Requires payloads registered under the stable discriminator declared by <typeparamref name="TData"/>'s
    /// <see cref="NotificationDataTypeAttribute"/>. Repeating the same contract is idempotent; a conflicting
    /// discriminator or CLR registration mapping is rejected.
    /// </summary>
    public NotificationDefinition WithPayload<TData>() where TData : NotificationData
    {
        var dataType = typeof(TData);
        var discriminator = NotificationDataTypeAttribute.GetNameOrNull(dataType)
            ?? throw new ArgumentException(
                $"'{dataType.FullName}' must be annotated with [NotificationDataType(\"...\")] " +
                "before it can be used by a notification definition.",
                nameof(TData));

        return SetPayloadContract(discriminator, dataType);
    }

    /// <summary>
    /// Requires payloads registered under the supplied stable discriminator. Repeating the same contract is
    /// idempotent and cannot weaken a preceding type-safe declaration.
    /// </summary>
    public NotificationDefinition WithPayload(string discriminator)
    {
        return SetPayloadContract(
            Check.NotNullOrWhiteSpace(discriminator, nameof(discriminator)),
            payloadType: null);
    }

    /// <summary>
    /// Declares the entity-identity contract. An expected entity type, when supplied, is a stable caller-chosen name
    /// such as <c>"Demo.Order"</c>, never a CLR <see cref="Type.FullName"/>. Repeating the same contract is
    /// idempotent; a conflicting repetition is rejected.
    /// </summary>
    public NotificationDefinition WithEntityContract(
        NotificationEntityRequirement requirement,
        string? expectedEntityTypeName = null)
    {
        if (requirement == NotificationEntityRequirement.Unspecified || !Enum.IsDefined(requirement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requirement),
                requirement,
                "Specify Forbidden, Optional, or Required. Omit WithEntityContract(...) for legacy behavior.");
        }

        if (requirement == NotificationEntityRequirement.Forbidden && expectedEntityTypeName != null)
        {
            throw new ArgumentException(
                "A definition that forbids entity identity cannot constrain an entity type name.",
                nameof(expectedEntityTypeName));
        }

        var normalizedEntityTypeName = expectedEntityTypeName == null
            ? null
            : Check.NotNullOrWhiteSpace(expectedEntityTypeName, nameof(expectedEntityTypeName));
        if (EntityRequirement != NotificationEntityRequirement.Unspecified)
        {
            if (EntityRequirement != requirement ||
                !StringComparer.Ordinal.Equals(ExpectedEntityTypeName, normalizedEntityTypeName))
            {
                throw new InvalidOperationException(
                    $"Notification definition '{Name}' already has entity contract " +
                    $"'{EntityRequirement}:{ExpectedEntityTypeName ?? "<any>"}' and cannot be changed to " +
                    $"'{requirement}:{normalizedEntityTypeName ?? "<any>"}'.");
            }

            return this;
        }

        EntityRequirement = requirement;
        ExpectedEntityTypeName = normalizedEntityTypeName;
        return this;
    }

    private NotificationDefinition SetPayloadContract(string discriminator, Type? payloadType)
    {
        if (PayloadDiscriminator == null)
        {
            PayloadDiscriminator = discriminator;
            PayloadType = payloadType;
            return this;
        }

        if (!StringComparer.Ordinal.Equals(PayloadDiscriminator, discriminator))
        {
            throw new InvalidOperationException(
                $"Notification definition '{Name}' already requires payload discriminator " +
                $"'{PayloadDiscriminator}' and cannot be changed to '{discriminator}'.");
        }

        if (PayloadType != null && payloadType != null && PayloadType != payloadType)
        {
            throw new InvalidOperationException(
                $"Notification definition '{Name}' already binds payload discriminator '{PayloadDiscriminator}' " +
                $"to CLR type '{PayloadType.FullName}' and cannot bind it to '{payloadType.FullName}'.");
        }

        // A same-discriminator string repeat must not erase a prior type-safe declaration. Conversely, a later
        // generic repeat may safely tighten a string-only declaration with its intended CLR registration mapping.
        PayloadType ??= payloadType;
        return this;
    }

    public NotificationDefinition WithDescription(ILocalizableString description)
    {
        Description = description;
        return this;
    }

    public NotificationDefinition WithAttribute(string key, object? value)
    {
        Attributes[key] = value;
        return this;
    }

    /// <summary>Routes delivery to specific external notifier channels (by name).</summary>
    public NotificationDefinition UseChannels(params string[] channels)
    {
        if (channels == null || channels.Length == 0)
        {
            throw new ArgumentException(
                "At least one notification channel must be specified. Omit UseChannels(...) for inbox-only notifications.",
                nameof(channels));
        }

        if (channels.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Notification channel names cannot be null, empty or whitespace.", nameof(channels));
        }

        Attributes[NotificationChannels.AttributeName] = channels.Select(channel => channel.Trim()).ToArray();
        return this;
    }

    public string[]? GetChannelsOrNull()
    {
        return Attributes.TryGetValue(NotificationChannels.AttributeName, out var value) ? value as string[] : null;
    }
}

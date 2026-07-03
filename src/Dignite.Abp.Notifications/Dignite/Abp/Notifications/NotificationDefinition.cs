using System;
using System.Collections.Generic;
using Volo.Abp.Localization;

namespace Dignite.Abp.Notifications;

/// <summary>
/// A notification type registered by a business module: its stable name, display text, and optional
/// permission/feature gating and free-form attributes (e.g. channel routing hints).
/// </summary>
public class NotificationDefinition
{
    public string Name { get; }

    public Type? EntityType { get; }

    public ILocalizableString DisplayName { get; set; }

    public ILocalizableString? Description { get; set; }

    /// <summary>When set, a user must be granted this permission for the notification to be available to them.</summary>
    public string? PermissionName { get; private set; }

    /// <summary>When set, this feature must be enabled for the notification to be available.</summary>
    public string? FeatureName { get; private set; }

    /// <summary>Free-form extension bag — e.g. channel white-lists for future routing.</summary>
    public IDictionary<string, object?> Attributes { get; }

    public NotificationDefinition(string name, ILocalizableString displayName, Type? entityType = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
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
}

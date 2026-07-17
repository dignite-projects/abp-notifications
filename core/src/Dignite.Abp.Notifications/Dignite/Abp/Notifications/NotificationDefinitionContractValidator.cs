using System;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

internal static class NotificationDefinitionContractValidator
{
    public static void ValidateRegistration(
        NotificationDefinition definition,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        if (definition.PayloadDiscriminator == null)
        {
            return;
        }

        var registeredType = dataTypeRegistry.GetTypeOrNull(definition.PayloadDiscriminator);
        if (registeredType == null)
        {
            throw new InvalidOperationException(
                $"Notification definition '{definition.Name}' references payload discriminator " +
                $"'{definition.PayloadDiscriminator}', but that discriminator is not registered in " +
                $"{nameof(NotificationDataOptions)}.");
        }

        if (definition.PayloadType != null && registeredType != definition.PayloadType)
        {
            throw new InvalidOperationException(
                $"Notification definition '{definition.Name}' declares payload discriminator " +
                $"'{definition.PayloadDiscriminator}' for CLR type '{GetTypeName(definition.PayloadType)}', " +
                $"but the discriminator is registered for '{GetTypeName(registeredType)}'.");
        }
    }

    public static void ValidatePublish(
        NotificationDefinition definition,
        NotificationData? data,
        NotificationEntityIdentifier? entityIdentifier,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        Validate(
            definition,
            data,
            entityIdentifier?.EntityTypeName,
            entityIdentifier?.EntityId,
            dataTypeRegistry);
    }

    public static void ValidateDistribution(
        NotificationDefinition definition,
        NotificationInfo notification,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        Validate(
            definition,
            notification.Data,
            notification.EntityTypeName,
            notification.EntityId,
            dataTypeRegistry);
    }

    private static void Validate(
        NotificationDefinition definition,
        NotificationData? data,
        string? entityTypeName,
        string? entityId,
        INotificationDataTypeRegistry dataTypeRegistry)
    {
        ValidateRegistration(definition, dataTypeRegistry);

        if (definition.PayloadDiscriminator != null)
        {
            if (data == null)
            {
                throw new AbpException(
                    $"Notification '{definition.Name}' requires payload discriminator " +
                    $"'{definition.PayloadDiscriminator}', but no payload was supplied.");
            }

            var actualDiscriminator = dataTypeRegistry.GetDiscriminatorOrNull(data.GetType());
            if (actualDiscriminator == null)
            {
                throw new AbpException(
                    $"Notification '{definition.Name}' received unregistered payload CLR type " +
                    $"'{GetTypeName(data.GetType())}'; expected discriminator '{definition.PayloadDiscriminator}'.");
            }

            if (!StringComparer.Ordinal.Equals(definition.PayloadDiscriminator, actualDiscriminator))
            {
                throw new AbpException(
                    $"Notification '{definition.Name}' requires payload discriminator " +
                    $"'{definition.PayloadDiscriminator}', but received '{actualDiscriminator}'.");
            }
        }

        if (definition.EntityRequirement == NotificationEntityRequirement.Unspecified)
        {
            return;
        }

        if ((entityTypeName == null) != (entityId == null))
        {
            throw new AbpException(
                $"Notification '{definition.Name}' has an incomplete entity identity. EntityTypeName and EntityId " +
                "must either both be supplied or both be null.");
        }

        var hasEntity = entityTypeName != null;
        switch (definition.EntityRequirement)
        {
            case NotificationEntityRequirement.Forbidden when hasEntity:
                throw new AbpException(
                    $"Notification '{definition.Name}' forbids an entity identity, but received " +
                    $"'{entityTypeName}:{entityId}'.");
            case NotificationEntityRequirement.Required when !hasEntity:
                throw new AbpException(
                    $"Notification '{definition.Name}' requires an entity identity, but none was supplied.");
        }

        if (hasEntity && definition.ExpectedEntityTypeName != null &&
            !StringComparer.Ordinal.Equals(definition.ExpectedEntityTypeName, entityTypeName))
        {
            throw new AbpException(
                $"Notification '{definition.Name}' requires entity type '{definition.ExpectedEntityTypeName}', " +
                $"but received '{entityTypeName}'. Stable entity type names use ordinal, " +
                "case-sensitive comparison.");
        }
    }

    private static string GetTypeName(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? "<unknown assembly>";
        return $"{type.FullName ?? type.Name}, {assemblyName}";
    }
}

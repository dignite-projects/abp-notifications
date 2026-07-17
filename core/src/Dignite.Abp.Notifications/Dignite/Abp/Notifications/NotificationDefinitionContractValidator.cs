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

        switch (definition.EntityRequirement)
        {
            case NotificationEntityRequirement.Unspecified:
                return;
            case NotificationEntityRequirement.Forbidden when entityIdentifier != null:
                throw new AbpException(
                    $"Notification '{definition.Name}' forbids an entity identity, but received " +
                    $"'{entityIdentifier.EntityTypeName}:{entityIdentifier.EntityId}'.");
            case NotificationEntityRequirement.Required when entityIdentifier == null:
                throw new AbpException(
                    $"Notification '{definition.Name}' requires an entity identity, but none was supplied.");
        }

        if (entityIdentifier != null && definition.ExpectedEntityTypeName != null &&
            !StringComparer.Ordinal.Equals(definition.ExpectedEntityTypeName, entityIdentifier.EntityTypeName))
        {
            throw new AbpException(
                $"Notification '{definition.Name}' requires entity type '{definition.ExpectedEntityTypeName}', " +
                $"but received '{entityIdentifier.EntityTypeName}'. Stable entity type names use ordinal, " +
                "case-sensitive comparison.");
        }
    }

    private static string GetTypeName(Type type)
    {
        var assemblyName = type.Assembly.GetName().Name ?? "<unknown assembly>";
        return $"{type.FullName ?? type.Name}, {assemblyName}";
    }
}

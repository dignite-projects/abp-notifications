using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Polymorphic (de)serializer for <see cref="NotificationData"/> driven by <see cref="INotificationDataTypeRegistry"/>.
/// Emits/reads a stable string discriminator ("type") instead of a CLR name / AssemblyQualifiedName, so payloads
/// survive assembly version changes and can be understood by any client sharing the same registry.
/// </summary>
public sealed class NotificationDataJsonConverter : JsonConverter<NotificationData>
{
    public const string DiscriminatorPropertyName = "type";

    private readonly INotificationDataTypeRegistry _registry;
    private JsonSerializerOptions? _innerOptions;

    public NotificationDataJsonConverter(INotificationDataTypeRegistry registry)
    {
        _registry = registry;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(NotificationData).IsAssignableFrom(typeToConvert);
    }

    public override NotificationData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty(DiscriminatorPropertyName, out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException(
                $"NotificationData JSON is missing a string '{DiscriminatorPropertyName}' discriminator.");
        }

        var discriminator = typeElement.GetString()!;
        var clrType = _registry.GetTypeOrNull(discriminator)
            ?? throw new JsonException(
                $"Unknown notification data type '{discriminator}'. " +
                "Register it via NotificationDataOptions so this consumer can deserialize it.");

        // Deserialize the concrete type from the payload minus the discriminator, so "type" does not leak into
        // ExtensionData. (MemoryStream keeps this working on netstandard2.0, where ArrayBufferWriter is not public.)
        byte[] payload;
        using (var stream = new MemoryStream())
        {
            using (var payloadWriter = new Utf8JsonWriter(stream))
            {
                payloadWriter.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, DiscriminatorPropertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    property.WriteTo(payloadWriter);
                }

                payloadWriter.WriteEndObject();
            }

            payload = stream.ToArray();
        }

        return (NotificationData?)JsonSerializer.Deserialize(payload, clrType, GetInnerOptions(options));
    }

    public override void Write(Utf8JsonWriter writer, NotificationData value, JsonSerializerOptions options)
    {
        var clrType = value.GetType();
        var discriminator = _registry.GetDiscriminatorOrNull(clrType)
            ?? throw new JsonException(
                $"Notification data type '{clrType.FullName}' is not registered. " +
                "Annotate it with [NotificationDataType(\"...\")] and register it via NotificationDataOptions.");

        using var doc = JsonSerializer.SerializeToDocument(value, clrType, GetInnerOptions(options));

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorPropertyName, discriminator);
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, DiscriminatorPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// A sibling options set WITHOUT this converter, used to (de)serialize the concrete type without re-entering here.
    /// </summary>
    private JsonSerializerOptions GetInnerOptions(JsonSerializerOptions options)
    {
        if (_innerOptions is null)
        {
            var inner = new JsonSerializerOptions(options);
            for (var i = inner.Converters.Count - 1; i >= 0; i--)
            {
                if (inner.Converters[i] is NotificationDataJsonConverter)
                {
                    inner.Converters.RemoveAt(i);
                }
            }

            _innerOptions = inner;
        }

        return _innerOptions;
    }
}

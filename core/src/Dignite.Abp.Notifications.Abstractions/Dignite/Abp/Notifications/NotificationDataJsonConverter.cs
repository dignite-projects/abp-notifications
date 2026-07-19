using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Polymorphic (de)serializer for <see cref="NotificationData"/>. It owns the stable <c>type</c> +
/// <c>schemaVersion</c> envelope, runs deterministic JSON upcasters, and never resolves CLR names from JSON.
/// The one-argument constructor remains strict; durable/event/HTTP boundaries explicitly opt into tolerant reads.
/// </summary>
public sealed class NotificationDataJsonConverter : JsonConverter<NotificationData>
{
    public const string DiscriminatorPropertyName = "type";
    public const string SchemaVersionPropertyName = "schemaVersion";

    private readonly INotificationDataTypeRegistry _registry;
    private readonly NotificationDataReadMode _readMode;
    private JsonSerializerOptions? _innerOptions;

    public NotificationDataJsonConverter(INotificationDataTypeRegistry registry)
        : this(registry, NotificationDataReadMode.Strict)
    {
    }

    public NotificationDataJsonConverter(
        INotificationDataTypeRegistry registry,
        NotificationDataReadMode readMode)
    {
        if (!Enum.IsDefined(typeof(NotificationDataReadMode), readMode))
        {
            throw new ArgumentOutOfRangeException(nameof(readMode), readMode, "Specify Strict or Tolerant.");
        }

        _registry = registry;
        _readMode = readMode;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(NotificationData).IsAssignableFrom(typeToConvert);
    }

    public override NotificationData? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.ParseValue(ref reader);
        }
        catch (JsonException exception)
        {
            return HandleFailure(
                UnsupportedNotificationDataReason.MalformedPayload,
                "NotificationData JSON is malformed.",
                rawJson: string.Empty,
                innerException: exception);
        }

        using (document)
        {
            var root = document.RootElement;
            var rawJson = root.GetRawText();
            if (root.ValueKind != JsonValueKind.Object)
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.MalformedPayload,
                    "NotificationData JSON must be an object.",
                    rawJson);
            }

            if (!root.TryGetProperty(DiscriminatorPropertyName, out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(typeElement.GetString()))
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.MalformedPayload,
                    $"NotificationData JSON is missing a non-empty string '{DiscriminatorPropertyName}' discriminator.",
                    rawJson);
            }

            var discriminator = typeElement.GetString()!;
            var schemaVersion = NotificationDataSchema.LegacyVersion;
            if (root.TryGetProperty(SchemaVersionPropertyName, out var versionElement) &&
                (versionElement.ValueKind != JsonValueKind.Number ||
                 !versionElement.TryGetInt32(out schemaVersion) ||
                 schemaVersion < NotificationDataSchema.LegacyVersion))
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.MalformedPayload,
                    $"NotificationData '{discriminator}' has an invalid '{SchemaVersionPropertyName}'.",
                    rawJson,
                    discriminator);
            }

            var clrType = _registry.GetTypeOrNull(discriminator);
            if (clrType == null)
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.UnknownDiscriminator,
                    $"Unknown notification data type '{discriminator}'. Register it via NotificationDataOptions.",
                    rawJson,
                    discriminator,
                    schemaVersion);
            }

            var currentVersion = _registry.GetCurrentSchemaVersion(discriminator);
            if (schemaVersion > currentVersion)
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.UnsupportedFutureVersion,
                    $"Notification data '{discriminator}' schema v{schemaVersion} is newer than this consumer's " +
                    $"current schema v{currentVersion}.",
                    rawJson,
                    discriminator,
                    schemaVersion);
            }

            JsonObject payload;
            try
            {
                payload = CreatePayloadObject(root);
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is InvalidOperationException ||
                exception is ArgumentException)
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.MalformedPayload,
                    $"Notification data '{discriminator}' could not be read as a JSON object.",
                    rawJson,
                    discriminator,
                    schemaVersion,
                    exception);
            }

            if (schemaVersion < currentVersion)
            {
                try
                {
                    payload = _registry.Upcast(discriminator, schemaVersion, payload);
                    EnsureNoReservedEnvelopeMembers(payload);
                }
                catch (Exception exception) when (IsRecoverableReadException(exception))
                {
                    return HandleFailure(
                        UnsupportedNotificationDataReason.UpcastFailed,
                        $"Notification data '{discriminator}' failed to upcast from schema v{schemaVersion} " +
                        $"to v{currentVersion}.",
                        rawJson,
                        discriminator,
                        schemaVersion,
                        exception);
                }
            }

            try
            {
                var data = (NotificationData?)JsonSerializer.Deserialize(
                    payload.ToJsonString(),
                    clrType,
                    GetInnerOptions(options));
                if (data == null)
                {
                    throw new JsonException(
                        $"Notification data '{discriminator}' deserialized to null.");
                }

                data.SchemaVersion = currentVersion;
                return data;
            }
            catch (Exception exception) when (IsRecoverableReadException(exception))
            {
                return HandleFailure(
                    UnsupportedNotificationDataReason.MalformedPayload,
                    $"Notification data '{discriminator}' schema v{schemaVersion} is malformed for the " +
                    $"registered current model v{currentVersion}.",
                    rawJson,
                    discriminator,
                    schemaVersion,
                    exception);
            }
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        NotificationData value,
        JsonSerializerOptions options)
    {
        var clrType = value.GetType();
        var discriminator = _registry.GetDiscriminatorOrNull(clrType)
            ?? throw new JsonException(
                $"Notification data type '{clrType.FullName}' is not registered. " +
                "Annotate it with [NotificationDataType(\"...\")] and register it via NotificationDataOptions.");
        var currentVersion = _registry.GetCurrentSchemaVersion(discriminator);

        using var document = JsonSerializer.SerializeToDocument(value, clrType, GetInnerOptions(options));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException(
                $"Notification data type '{clrType.FullName}' must serialize as a JSON object.");
        }

        writer.WriteStartObject();
        writer.WriteString(DiscriminatorPropertyName, discriminator);
        writer.WriteNumber(SchemaVersionPropertyName, currentVersion);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (IsReservedEnvelopeMember(property.Name))
            {
                continue;
            }

            property.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    private NotificationData HandleFailure(
        UnsupportedNotificationDataReason reason,
        string message,
        string rawJson,
        string? discriminator = null,
        int? schemaVersion = null,
        Exception? innerException = null)
    {
        if (_readMode == NotificationDataReadMode.Strict)
        {
            throw new NotificationDataReadException(
                reason,
                message,
                discriminator,
                schemaVersion,
                innerException);
        }

        return new UnsupportedNotificationData
        {
            OriginalDiscriminator = discriminator,
            OriginalSchemaVersion = schemaVersion,
            Reason = reason,
            RawJson = rawJson
        };
    }

    private static JsonObject CreatePayloadObject(JsonElement root)
    {
        var payload = new JsonObject();
        foreach (var property in root.EnumerateObject())
        {
            if (IsReservedEnvelopeMember(property.Name))
            {
                continue;
            }

            payload.Add(property.Name, JsonNode.Parse(property.Value.GetRawText()));
        }

        return payload;
    }

    private static void EnsureNoReservedEnvelopeMembers(JsonObject payload)
    {
        var reservedMember = payload
            .Select(pair => pair.Key)
            .FirstOrDefault(IsReservedEnvelopeMember);
        if (reservedMember != null)
        {
            throw new InvalidOperationException(
                $"An upcaster returned reserved envelope member '{reservedMember}'. " +
                $"Upcasters may only transform payload members.");
        }
    }

    private static bool IsReservedEnvelopeMember(string propertyName)
    {
        return string.Equals(propertyName, DiscriminatorPropertyName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, SchemaVersionPropertyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecoverableReadException(Exception exception)
    {
        return exception is not OperationCanceledException &&
               exception is not OutOfMemoryException &&
               exception is not StackOverflowException &&
               exception is not AccessViolationException &&
               exception is not AppDomainUnloadedException &&
               exception is not BadImageFormatException &&
               exception is not CannotUnloadAppDomainException &&
               exception is not InvalidProgramException;
    }

    /// <summary>
    /// A sibling options set without this converter, used to (de)serialize the concrete type without re-entering here.
    /// </summary>
    private JsonSerializerOptions GetInnerOptions(JsonSerializerOptions options)
    {
        if (_innerOptions is null)
        {
            var inner = new JsonSerializerOptions(options);
            for (var index = inner.Converters.Count - 1; index >= 0; index--)
            {
                if (inner.Converters[index] is NotificationDataJsonConverter)
                {
                    inner.Converters.RemoveAt(index);
                }
            }

            _innerOptions = inner;
        }

        return _innerOptions;
    }
}

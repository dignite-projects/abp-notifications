using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

[ExposeServices(
    typeof(INotificationDataSerializer),
    typeof(INotificationDataTolerantReader),
    typeof(NotificationDataSerializer))]
public class NotificationDataSerializer :
    INotificationDataSerializer,
    INotificationDataTolerantReader,
    ISingletonDependency
{
    private readonly JsonSerializerOptions _strictOptions;
    private readonly JsonSerializerOptions _tolerantOptions;

    public NotificationDataSerializer(INotificationDataTypeRegistry registry)
    {
        _strictOptions = CreateOptions(registry, NotificationDataReadMode.Strict);
        _tolerantOptions = CreateOptions(registry, NotificationDataReadMode.Tolerant);
    }

    private static JsonSerializerOptions CreateOptions(
        INotificationDataTypeRegistry registry,
        NotificationDataReadMode readMode)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new NotificationDataJsonConverter(registry, readMode));
        return options;
    }

    public string? Serialize(NotificationData? data)
    {
        return data is null ? null : JsonSerializer.Serialize(data, _strictOptions);
    }

    public NotificationData? Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json)
            ? null
            : JsonSerializer.Deserialize<NotificationData>(json!, _strictOptions);
    }

    public NotificationData? DeserializeTolerantly(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var data = JsonSerializer.Deserialize<NotificationData>(json!, _tolerantOptions);
            if (data is UnsupportedNotificationData { RawJson.Length: 0 } unsupported)
            {
                unsupported.RawJson = json!;
            }

            return data;
        }
        catch (Exception exception) when (IsRecoverableReadException(exception))
        {
            return new UnsupportedNotificationData
            {
                Reason = UnsupportedNotificationDataReason.MalformedPayload,
                RawJson = json!
            };
        }
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
}

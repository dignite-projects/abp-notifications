using System.Text.Json;
using System.Text.Json.Serialization;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

public class NotificationDataSerializer : INotificationDataSerializer, ISingletonDependency
{
    private readonly JsonSerializerOptions _options;

    public NotificationDataSerializer(INotificationDataTypeRegistry registry)
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _options.Converters.Add(new NotificationDataJsonConverter(registry));
    }

    public string? Serialize(NotificationData? data)
    {
        return data is null ? null : JsonSerializer.Serialize(data, _options);
    }

    public NotificationData? Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<NotificationData>(json!, _options);
    }
}

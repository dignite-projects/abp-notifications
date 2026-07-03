using System;
using Microsoft.Extensions.Options;

namespace Dignite.Abp.Notifications;

/// <summary>A custom notification payload standing in for one a business module would define.</summary>
[NotificationDataType("Test.OrderShipped")]
public class OrderShippedNotificationData : NotificationData
{
    public string OrderNumber { get; set; } = default!;

    public int ItemCount { get; set; }
}

internal static class NotificationTestObjects
{
    public static NotificationDataTypeRegistry CreateRegistry(params Type[] extraTypes)
    {
        var options = new NotificationDataOptions();
        options.Add<MessageNotificationData>();
        options.Add<LocalizableMessageNotificationData>();
        foreach (var type in extraTypes)
        {
            options.Add(type);
        }

        return new NotificationDataTypeRegistry(Options.Create(options));
    }

    public static NotificationDataSerializer CreateSerializer(params Type[] extraTypes)
    {
        return new NotificationDataSerializer(CreateRegistry(extraTypes));
    }
}

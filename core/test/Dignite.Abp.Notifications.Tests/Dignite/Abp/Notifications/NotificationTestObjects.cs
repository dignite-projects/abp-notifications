using System;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Dignite.Abp.Notifications;

/// <summary>A custom notification payload standing in for one a business module would define.</summary>
[NotificationDataType("Test.OrderShipped")]
public class OrderShippedNotificationData : NotificationData
{
    public string OrderNumber { get; set; } = default!;

    public int ItemCount { get; set; }
}

[NotificationDataType("Test.EvolvingOrder", 3)]
public class EvolvingOrderNotificationData : NotificationData
{
    public string OrderId { get; set; } = default!;

    public int Quantity { get; set; }
}

[NotificationDataType("Test.Rolling", 1)]
public class RollingNotificationDataV1 : NotificationData
{
    public string Message { get; set; } = default!;
}

[NotificationDataType("Test.Rolling", 2)]
public class RollingNotificationDataV2 : NotificationData
{
    public string Text { get; set; } = default!;

    public int Importance { get; set; }
}

[NotificationDataType("Test.Failing", 2)]
public class FailingUpcastNotificationData : NotificationData
{
    public string Value { get; set; } = default!;
}

internal static class NotificationTestObjects
{
    public static NotificationDataTypeRegistry CreateRegistry(params Type[] extraTypes)
    {
        var options = CreateBaseOptions();
        foreach (var type in extraTypes)
        {
            options.Add(type);
        }

        return new NotificationDataTypeRegistry(Options.Create(options));
    }

    public static NotificationDataTypeRegistry CreateRegistry(
        Action<NotificationDataOptions> configure)
    {
        var options = CreateBaseOptions();
        configure(options);
        return new NotificationDataTypeRegistry(Options.Create(options));
    }

    public static NotificationDataSerializer CreateSerializer(params Type[] extraTypes)
    {
        return new NotificationDataSerializer(CreateRegistry(extraTypes));
    }

    public static NotificationDataSerializer CreateSerializer(
        Action<NotificationDataOptions> configure)
    {
        return new NotificationDataSerializer(CreateRegistry(configure));
    }

    public static void AddEvolvingOrder(NotificationDataOptions options)
    {
        options.Add<EvolvingOrderNotificationData>();
        options.AddUpcaster<EvolvingOrderNotificationData>(1, payload =>
        {
            payload["orderId"] = payload["orderNumber"]?.DeepClone();
            payload.Remove("orderNumber");
            return payload;
        });
        options.AddUpcaster<EvolvingOrderNotificationData>(2, payload =>
        {
            payload["quantity"] = payload["itemCount"]?.DeepClone();
            payload.Remove("itemCount");
            return payload;
        });
    }

    public static void AddRollingV2(NotificationDataOptions options)
    {
        options.Add<RollingNotificationDataV2>();
        options.AddUpcaster<RollingNotificationDataV2>(1, payload =>
        {
            payload["text"] = payload["message"]?.DeepClone();
            payload.Remove("message");
            payload["importance"] = 0;
            return payload;
        });
    }

    private static NotificationDataOptions CreateBaseOptions()
    {
        var options = new NotificationDataOptions();
        options.Add<MessageNotificationData>();
        options.Add<LocalizableMessageNotificationData>();
        options.Add<UnsupportedNotificationData>();
        return options;
    }
}

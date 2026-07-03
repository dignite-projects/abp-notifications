using System;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationDataSerializationTests
{
    [Fact]
    public void Custom_data_round_trips_across_independent_registries()
    {
        // Two serializers built independently — the writer (server) and reader (a remote HttpApi.Client consumer).
        var server = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));
        var client = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var json = server.Serialize(new OrderShippedNotificationData { OrderNumber = "SO-1001", ItemCount = 3 });

        json.ShouldNotBeNull();
        var back = client.Deserialize(json).ShouldBeOfType<OrderShippedNotificationData>();
        back.OrderNumber.ShouldBe("SO-1001");
        back.ItemCount.ShouldBe(3);
    }

    [Fact]
    public void Wire_format_uses_a_stable_discriminator_and_no_clr_type_name()
    {
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var json = serializer.Serialize(new OrderShippedNotificationData { OrderNumber = "SO-1", ItemCount = 1 })!;

        json.ShouldContain("\"type\":\"Test.OrderShipped\"");
        // The two footguns from the reference implementation must be gone:
        json.ShouldNotContain("Version=");                       // no AssemblyQualifiedName
        json.ShouldNotContain("OrderShippedNotificationData");   // no CLR type name on the wire
    }

    [Fact]
    public void Reads_data_written_by_another_assembly_version()
    {
        // JSON as persisted by some earlier/other build. Only the stable discriminator identifies the type —
        // there is no assembly version to break Type.GetType(), so historical data stays readable.
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"schemaVersion\":1,\"orderNumber\":\"SO-42\",\"itemCount\":7}";

        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var data = serializer.Deserialize(json).ShouldBeOfType<OrderShippedNotificationData>();
        data.OrderNumber.ShouldBe("SO-42");
        data.ItemCount.ShouldBe(7);
    }

    [Fact]
    public void Unregistered_type_throws_a_clear_error_on_read()
    {
        // Built-ins only — OrderShipped is NOT registered on this consumer.
        var serializer = NotificationTestObjects.CreateSerializer();
        const string json = "{\"type\":\"Test.OrderShipped\",\"orderNumber\":\"x\"}";

        var ex = Should.Throw<JsonException>(() => serializer.Deserialize(json));
        ex.Message.ShouldContain("Test.OrderShipped");
    }

    [Fact]
    public void Unregistered_type_throws_a_clear_error_on_write()
    {
        var serializer = NotificationTestObjects.CreateSerializer(); // OrderShipped not registered

        Should.Throw<JsonException>(() =>
            serializer.Serialize(new OrderShippedNotificationData { OrderNumber = "x" }));
    }

    [Fact]
    public void Missing_discriminator_throws()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        Should.Throw<JsonException>(() => serializer.Deserialize("{\"message\":\"x\"}"));
    }

    [Fact]
    public void Null_and_empty_are_handled()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        serializer.Serialize(null).ShouldBeNull();
        serializer.Deserialize(null).ShouldBeNull();
        serializer.Deserialize("").ShouldBeNull();
    }

    [Fact]
    public void Unknown_properties_from_a_newer_schema_are_preserved()
    {
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"schemaVersion\":2,\"orderNumber\":\"SO-9\",\"itemCount\":1," +
            "\"trackingUrl\":\"http://x\"}";

        var data = serializer.Deserialize(json).ShouldBeOfType<OrderShippedNotificationData>();

        data.SchemaVersion.ShouldBe(2);
        data.ExtensionData.ShouldNotBeNull();
        data.ExtensionData!.ShouldContainKey("trackingUrl");
    }

    [Fact]
    public void Builtin_message_data_round_trips()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        var json = serializer.Serialize(new MessageNotificationData("hello"))!;

        json.ShouldContain("\"type\":\"Dignite.Message\"");
        serializer.Deserialize(json).ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hello");
    }

    [Fact]
    public void Notification_data_nested_in_an_eto_round_trips_polymorphically()
    {
        // Proves the converter also works when NotificationData is nested (as in RealTimeNotifyEto / DTOs),
        // not just at the top level.
        var registry = NotificationTestObjects.CreateRegistry(typeof(OrderShippedNotificationData));
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new NotificationDataJsonConverter(registry));

        var eto = new RealTimeNotifyEto(
            Guid.NewGuid(),
            "order.shipped",
            new OrderShippedNotificationData { OrderNumber = "SO-7", ItemCount = 2 },
            NotificationSeverity.Info,
            DateTime.UtcNow,
            new[] { Guid.NewGuid() });

        var json = JsonSerializer.Serialize(eto, options);
        var back = JsonSerializer.Deserialize<RealTimeNotifyEto>(json, options)!;

        back.Data.ShouldBeOfType<OrderShippedNotificationData>().OrderNumber.ShouldBe("SO-7");
    }
}

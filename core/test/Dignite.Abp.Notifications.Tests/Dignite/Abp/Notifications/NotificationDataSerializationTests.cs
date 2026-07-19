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
        var server = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));
        var client = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var json = server.Serialize(new OrderShippedNotificationData
        {
            OrderNumber = "SO-1001",
            ItemCount = 3
        });

        json.ShouldNotBeNull();
        var back = client.Deserialize(json, NotificationDataReadMode.Strict)
            .ShouldBeOfType<OrderShippedNotificationData>();
        back.OrderNumber.ShouldBe("SO-1001");
        back.ItemCount.ShouldBe(3);
    }

    [Fact]
    public void New_writes_use_the_stable_discriminator_and_no_clr_name()
    {
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var json = serializer.Serialize(new OrderShippedNotificationData
        {
            OrderNumber = "SO-1",
            ItemCount = 1
        })!;

        json.ShouldContain("\"type\":\"Test.OrderShipped\"");
        json.ShouldNotContain("Version=");
        json.ShouldNotContain("OrderShippedNotificationData");
    }

    [Fact]
    public void Unknown_discriminator_is_distinguishable_in_strict_mode_and_safe_in_tolerant_mode()
    {
        const string json = "{\"type\":\"Vendor.Removed\",\"secret\":\"x\"}";
        var serializer = NotificationTestObjects.CreateSerializer();

        var exception = Should.Throw<NotificationDataReadException>(() =>
            serializer.Deserialize(json, NotificationDataReadMode.Strict));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        exception.Discriminator.ShouldBe("Vendor.Removed");

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        unsupported.OriginalDiscriminator.ShouldBe("Vendor.Removed");
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Malformed_known_payload_is_distinguishable_and_preserves_raw_json()
    {
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"orderNumber\":\"SO-1\",\"itemCount\":\"bad\"}";
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var exception = Should.Throw<NotificationDataReadException>(() =>
            serializer.Deserialize(json, NotificationDataReadMode.Strict));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        exception.Discriminator.ShouldBe("Test.OrderShipped");

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        unsupported.OriginalDiscriminator.ShouldBe("Test.OrderShipped");
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Ordinary_model_materialization_exceptions_are_typed_or_tolerated_without_failing_a_batch()
    {
        const string json = "{\"type\":\"Test.ThrowingSetter\",\"value\":\"bad\"}";
        var serializer = NotificationTestObjects.CreateSerializer(typeof(ThrowingSetterNotificationData));

        var exception = Should.Throw<NotificationDataReadException>(() =>
            serializer.Deserialize(json, NotificationDataReadMode.Strict));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        exception.InnerException.ShouldBeOfType<FormatException>();

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        unsupported.OriginalDiscriminator.ShouldBe("Test.ThrowingSetter");
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Unsupported_placeholder_round_trips_as_a_known_safe_type_without_activating_original_type()
    {
        const string original = "{\"type\":\"Unknown.ArbitraryClrLookingName\",\"value\":42}";
        var serializer = NotificationTestObjects.CreateSerializer();
        var unsupported = Tolerant(serializer, original).ShouldBeOfType<UnsupportedNotificationData>();

        var json = serializer.Serialize(unsupported)!;
        var roundTrip = serializer.Deserialize(json, NotificationDataReadMode.Strict)
            .ShouldBeOfType<UnsupportedNotificationData>();

        json.ShouldContain("\"type\":\"Dignite.Unsupported\"");
        json.ShouldContain("\"originalDiscriminator\":\"Unknown.ArbitraryClrLookingName\"");
        json.ShouldContain("\"rawJson\":");
        roundTrip.OriginalDiscriminator.ShouldBe("Unknown.ArbitraryClrLookingName");
        roundTrip.RawJson.ShouldBe(original);
    }

    [Fact]
    public void Unknown_properties_are_preserved_as_extension_data()
    {
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"orderNumber\":\"SO-9\",\"itemCount\":1,\"trackingUrl\":\"http://x\"}";

        var data = serializer.Deserialize(json, NotificationDataReadMode.Strict)
            .ShouldBeOfType<OrderShippedNotificationData>();

        data.ExtensionData.ShouldNotBeNull();
        data.ExtensionData!.ShouldContainKey("trackingUrl");
    }

    [Fact]
    public void Builtin_message_data_round_trips()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        var json = serializer.Serialize(new MessageNotificationData("hello"))!;

        json.ShouldContain("\"type\":\"Dignite.Message\"");
        serializer.Deserialize(json, NotificationDataReadMode.Strict)
            .ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hello");
    }

    [Fact]
    public void Notification_data_nested_in_an_eto_round_trips_polymorphically()
    {
        var registry = NotificationTestObjects.CreateRegistry(typeof(OrderShippedNotificationData));
        var options = CreateJsonOptions(registry, NotificationDataReadMode.Tolerant);
        var eto = NewEvent(new OrderShippedNotificationData
        {
            OrderNumber = "SO-7",
            ItemCount = 2
        });

        var json = JsonSerializer.Serialize(eto, options);
        var back = JsonSerializer.Deserialize<NotificationDeliveryRequestedEto>(json, options)!;

        back.Data.ShouldBeOfType<OrderShippedNotificationData>().OrderNumber.ShouldBe("SO-7");
    }

    [Fact]
    public void Unknown_producer_type_becomes_unsupported_on_a_tolerant_consumer()
    {
        var producerRegistry = NotificationTestObjects.CreateRegistry(typeof(OrderShippedNotificationData));
        var consumerRegistry = NotificationTestObjects.CreateRegistry();
        var producerOptions = CreateJsonOptions(producerRegistry, NotificationDataReadMode.Strict);
        var consumerOptions = CreateJsonOptions(consumerRegistry, NotificationDataReadMode.Tolerant);
        var json = JsonSerializer.Serialize(
            NewEvent(new OrderShippedNotificationData { OrderNumber = "SO-7", ItemCount = 2 }),
            producerOptions);

        var received = JsonSerializer.Deserialize<NotificationDeliveryRequestedEto>(json, consumerOptions)!;

        var unsupported = received.Data.ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        unsupported.OriginalDiscriminator.ShouldBe("Test.OrderShipped");
    }

    [Fact]
    public void Missing_discriminator_is_malformed_in_strict_mode_and_tolerated_for_batch_reads()
    {
        const string json = "{\"message\":\"x\"}";
        var serializer = NotificationTestObjects.CreateSerializer();

        Should.Throw<NotificationDataReadException>(() =>
                serializer.Deserialize(json, NotificationDataReadMode.Strict))
            .Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>()
            .Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
    }

    [Fact]
    public void Null_and_empty_are_handled()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        serializer.Serialize(null).ShouldBeNull();
        serializer.Deserialize(null, NotificationDataReadMode.Strict).ShouldBeNull();
        serializer.Deserialize("", NotificationDataReadMode.Strict).ShouldBeNull();
        Tolerant(serializer, null).ShouldBeNull();
        Tolerant(serializer, string.Empty).ShouldBeNull();
    }

    [Fact]
    public void Unknown_read_mode_is_rejected_even_for_an_empty_payload()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        Should.Throw<ArgumentOutOfRangeException>(() =>
            serializer.Deserialize(null, (NotificationDataReadMode)99));
    }

    private static NotificationData? Tolerant(NotificationDataSerializer serializer, string? json)
    {
        return serializer.Deserialize(json, NotificationDataReadMode.Tolerant);
    }

    private static JsonSerializerOptions CreateJsonOptions(
        INotificationDataTypeRegistry registry,
        NotificationDataReadMode readMode)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new NotificationDataJsonConverter(registry, readMode));
        return options;
    }

    private static NotificationDeliveryRequestedEto NewEvent(NotificationData data)
    {
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string channel = "test";
        return new NotificationDeliveryRequestedEto
        {
            NotificationId = notificationId,
            NotificationName = "test",
            Data = data,
            Severity = NotificationSeverity.Info,
            CreationTime = DateTime.UtcNow,
            UserId = userId,
            Channel = channel
        };
    }
}

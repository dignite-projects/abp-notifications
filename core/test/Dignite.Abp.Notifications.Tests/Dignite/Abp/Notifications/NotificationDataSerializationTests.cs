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
        var back = client.Deserialize(json).ShouldBeOfType<OrderShippedNotificationData>();
        back.OrderNumber.ShouldBe("SO-1001");
        back.ItemCount.ShouldBe(3);
        back.SchemaVersion.ShouldBe(1);
    }

    [Fact]
    public void New_writes_use_stable_discriminator_explicit_current_version_and_no_clr_name()
    {
        var serializer = NotificationTestObjects.CreateSerializer(NotificationTestObjects.AddEvolvingOrder);

        var json = serializer.Serialize(new EvolvingOrderNotificationData
        {
            OrderId = "SO-1",
            Quantity = 1,
            SchemaVersion = 99
        })!;

        json.ShouldContain("\"type\":\"Test.EvolvingOrder\"");
        json.ShouldContain("\"schemaVersion\":3");
        json.ShouldNotContain("Version=");
        json.ShouldNotContain("EvolvingOrderNotificationData");
    }

    [Fact]
    public void Versionless_legacy_payload_uses_v1_and_multi_step_upcasts_to_current()
    {
        const string json =
            "{\"type\":\"Test.EvolvingOrder\",\"orderNumber\":\"SO-42\",\"itemCount\":7}";
        var serializer = NotificationTestObjects.CreateSerializer(NotificationTestObjects.AddEvolvingOrder);

        var data = serializer.Deserialize(json).ShouldBeOfType<EvolvingOrderNotificationData>();

        data.SchemaVersion.ShouldBe(3);
        data.OrderId.ShouldBe("SO-42");
        data.Quantity.ShouldBe(7);
    }

    [Fact]
    public void Explicit_old_version_multi_step_upcasts_deterministically()
    {
        const string json =
            "{\"type\":\"Test.EvolvingOrder\",\"schemaVersion\":1,\"orderNumber\":\"SO-9\",\"itemCount\":2}";
        var serializer = NotificationTestObjects.CreateSerializer(NotificationTestObjects.AddEvolvingOrder);

        var first = serializer.Deserialize(json).ShouldBeOfType<EvolvingOrderNotificationData>();
        var second = serializer.Deserialize(json).ShouldBeOfType<EvolvingOrderNotificationData>();

        first.OrderId.ShouldBe("SO-9");
        first.Quantity.ShouldBe(2);
        second.OrderId.ShouldBe(first.OrderId);
        second.Quantity.ShouldBe(first.Quantity);
    }

    [Fact]
    public void Upcaster_registration_order_does_not_change_numeric_execution_order()
    {
        const string json =
            "{\"type\":\"Test.EvolvingOrder\",\"schemaVersion\":1,\"orderNumber\":\"SO-ORDER\",\"itemCount\":4}";
        var serializer = NotificationTestObjects.CreateSerializer(options =>
        {
            options.Add<EvolvingOrderNotificationData>();
            options.AddUpcaster<EvolvingOrderNotificationData>(2, payload =>
            {
                payload.ContainsKey("orderId").ShouldBeTrue();
                payload["quantity"] = payload["itemCount"]?.DeepClone();
                payload.Remove("itemCount");
                return payload;
            });
            options.AddUpcaster<EvolvingOrderNotificationData>(1, payload =>
            {
                payload["orderId"] = payload["orderNumber"]?.DeepClone();
                payload.Remove("orderNumber");
                return payload;
            });
        });

        var data = serializer.Deserialize(json).ShouldBeOfType<EvolvingOrderNotificationData>();

        data.OrderId.ShouldBe("SO-ORDER");
        data.Quantity.ShouldBe(4);
    }

    [Fact]
    public void Unknown_discriminator_is_distinguishable_in_strict_mode_and_safe_in_tolerant_mode()
    {
        const string json = "{\"type\":\"Vendor.Removed\",\"schemaVersion\":2,\"secret\":\"x\"}";
        var serializer = NotificationTestObjects.CreateSerializer();

        var exception = Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        exception.Discriminator.ShouldBe("Vendor.Removed");
        exception.SchemaVersion.ShouldBe(2);

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        unsupported.OriginalDiscriminator.ShouldBe("Vendor.Removed");
        unsupported.OriginalSchemaVersion.ShouldBe(2);
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Unsupported_future_version_is_distinguishable_without_materializing_the_known_type()
    {
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"schemaVersion\":99,\"orderNumber\":\"future\",\"itemCount\":1}";
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var exception = Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.UnsupportedFutureVersion);
        exception.Discriminator.ShouldBe("Test.OrderShipped");
        exception.SchemaVersion.ShouldBe(99);

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnsupportedFutureVersion);
        unsupported.OriginalSchemaVersion.ShouldBe(99);
    }

    [Fact]
    public void Malformed_known_payload_is_distinguishable_and_preserves_raw_json()
    {
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"schemaVersion\":1,\"orderNumber\":\"SO-1\",\"itemCount\":\"bad\"}";
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));

        var exception = Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json));
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
        const string json =
            "{\"type\":\"Test.ThrowingSetter\",\"schemaVersion\":1,\"value\":\"bad\"}";
        var serializer = NotificationTestObjects.CreateSerializer(typeof(ThrowingSetterNotificationData));

        var exception = Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        exception.InnerException.ShouldBeOfType<FormatException>();

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        unsupported.OriginalDiscriminator.ShouldBe("Test.ThrowingSetter");
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Failed_upcast_is_distinguishable_from_malformed_current_data()
    {
        const string json =
            "{\"type\":\"Test.Failing\",\"schemaVersion\":1,\"value\":\"old\"}";
        var serializer = NotificationTestObjects.CreateSerializer(options =>
        {
            options.Add<FailingUpcastNotificationData>();
            options.AddUpcaster<FailingUpcastNotificationData>(1, _ =>
                throw new InvalidOperationException("simulated upcast failure"));
        });

        var exception = Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json));
        exception.Reason.ShouldBe(UnsupportedNotificationDataReason.UpcastFailed);

        var unsupported = Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UpcastFailed);
        unsupported.OriginalDiscriminator.ShouldBe("Test.Failing");
        unsupported.RawJson.ShouldBe(json);
    }

    [Fact]
    public void Unsupported_placeholder_round_trips_as_a_known_safe_type_without_activating_original_type()
    {
        const string original =
            "{\"type\":\"Unknown.ArbitraryClrLookingName\",\"schemaVersion\":1,\"value\":42}";
        var serializer = NotificationTestObjects.CreateSerializer();
        var unsupported = Tolerant(serializer, original).ShouldBeOfType<UnsupportedNotificationData>();

        var json = serializer.Serialize(unsupported)!;
        var roundTrip = serializer.Deserialize(json).ShouldBeOfType<UnsupportedNotificationData>();

        json.ShouldContain("\"type\":\"Dignite.Unsupported\"");
        json.ShouldContain("\"originalDiscriminator\":\"Unknown.ArbitraryClrLookingName\"");
        json.ShouldContain("\"rawJson\":");
        roundTrip.OriginalDiscriminator.ShouldBe("Unknown.ArbitraryClrLookingName");
        roundTrip.RawJson.ShouldBe(original);
    }

    [Fact]
    public void Same_version_unknown_properties_are_preserved_as_extension_data()
    {
        var serializer = NotificationTestObjects.CreateSerializer(typeof(OrderShippedNotificationData));
        const string json =
            "{\"type\":\"Test.OrderShipped\",\"schemaVersion\":1,\"orderNumber\":\"SO-9\",\"itemCount\":1," +
            "\"trackingUrl\":\"http://x\"}";

        var data = serializer.Deserialize(json).ShouldBeOfType<OrderShippedNotificationData>();

        data.SchemaVersion.ShouldBe(1);
        data.ExtensionData.ShouldNotBeNull();
        data.ExtensionData!.ShouldContainKey("trackingUrl");
    }

    [Fact]
    public void Builtin_message_data_round_trips()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        var json = serializer.Serialize(new MessageNotificationData("hello"))!;

        json.ShouldContain("\"type\":\"Dignite.Message\"");
        json.ShouldContain("\"schemaVersion\":1");
        serializer.Deserialize(json).ShouldBeOfType<MessageNotificationData>().Message.ShouldBe("hello");
    }

    [Fact]
    public void Old_producer_event_upcasts_on_a_new_consumer()
    {
        var oldRegistry = NotificationTestObjects.CreateRegistry(typeof(RollingNotificationDataV1));
        var newRegistry = NotificationTestObjects.CreateRegistry(NotificationTestObjects.AddRollingV2);
        var oldOptions = CreateJsonOptions(oldRegistry, NotificationDataReadMode.Strict);
        var newOptions = CreateJsonOptions(newRegistry, NotificationDataReadMode.Tolerant);
        var eto = NewEvent(new RollingNotificationDataV1 { Message = "from old" });
        var oldJson = JsonSerializer.Serialize(eto, oldOptions)
            .Replace("\"schemaVersion\":1,", string.Empty, StringComparison.Ordinal);

        var received = JsonSerializer.Deserialize<NotificationDeliveryRequestedEto>(oldJson, newOptions)!;

        var data = received.Data.ShouldBeOfType<RollingNotificationDataV2>();
        data.Text.ShouldBe("from old");
        data.Importance.ShouldBe(0);
        data.SchemaVersion.ShouldBe(2);
    }

    [Fact]
    public void New_producer_event_becomes_unsupported_on_an_older_schema_aware_consumer()
    {
        var newRegistry = NotificationTestObjects.CreateRegistry(NotificationTestObjects.AddRollingV2);
        var oldRegistry = NotificationTestObjects.CreateRegistry(typeof(RollingNotificationDataV1));
        var newOptions = CreateJsonOptions(newRegistry, NotificationDataReadMode.Strict);
        var oldOptions = CreateJsonOptions(oldRegistry, NotificationDataReadMode.Tolerant);
        var json = JsonSerializer.Serialize(
            NewEvent(new RollingNotificationDataV2 { Text = "from new", Importance = 5 }),
            newOptions);

        var received = JsonSerializer.Deserialize<NotificationDeliveryRequestedEto>(json, oldOptions)!;

        var unsupported = received.Data.ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnsupportedFutureVersion);
        unsupported.OriginalDiscriminator.ShouldBe("Test.Rolling");
        unsupported.OriginalSchemaVersion.ShouldBe(2);
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
    public void Missing_discriminator_is_malformed_in_strict_mode_and_tolerated_for_batch_reads()
    {
        const string json = "{\"message\":\"x\"}";
        var serializer = NotificationTestObjects.CreateSerializer();

        Should.Throw<NotificationDataReadException>(() => serializer.Deserialize(json))
            .Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
        Tolerant(serializer, json).ShouldBeOfType<UnsupportedNotificationData>()
            .Reason.ShouldBe(UnsupportedNotificationDataReason.MalformedPayload);
    }

    [Fact]
    public void Null_and_empty_are_handled()
    {
        var serializer = NotificationTestObjects.CreateSerializer();

        serializer.Serialize(null).ShouldBeNull();
        serializer.Deserialize(null).ShouldBeNull();
        serializer.Deserialize("").ShouldBeNull();
        Tolerant(serializer, null).ShouldBeNull();
        Tolerant(serializer, string.Empty).ShouldBeNull();
    }

    private static NotificationData? Tolerant(NotificationDataSerializer serializer, string? json)
    {
        return ((INotificationDataTolerantReader)serializer).DeserializeTolerantly(json);
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
            DeliveryId = NotificationDeliveryIdentity.CreateId(null, notificationId, userId, channel),
            IdempotencyKey = NotificationDeliveryIdentity.CreateIdempotencyKey(null, notificationId, userId, channel),
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

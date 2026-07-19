using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.TestProviderA;
using Dignite.Abp.Notifications.TestProviderB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Xunit;

namespace Dignite.Abp.Notifications;

public class NotificationRegistration_Tests
{
    [Fact]
    public void Duplicate_data_discriminator_reports_both_types_independent_of_registration_order()
    {
        var forward = Should.Throw<InvalidOperationException>(() =>
        {
            var options = new NotificationDataOptions();
            options.Add<DuplicateDataA>();
            options.Add<DuplicateDataB>();
        });
        var reverse = Should.Throw<InvalidOperationException>(() =>
        {
            var options = new NotificationDataOptions();
            options.Add<DuplicateDataB>();
            options.Add<DuplicateDataA>();
        });

        forward.Message.ShouldBe(reverse.Message);
        forward.Message.ShouldContain("Test.Duplicate");
        forward.Message.ShouldContain(typeof(DuplicateDataA).FullName!);
        forward.Message.ShouldContain(typeof(DuplicateDataB).FullName!);
    }

    [Fact]
    public void One_data_type_cannot_be_registered_under_two_discriminators()
    {
        var forward = Should.Throw<InvalidOperationException>(() =>
        {
            var options = new NotificationDataOptions();
            options.Add("Test.First", typeof(DistinctDataA));
            options.Add("Test.Second", typeof(DistinctDataA));
        });
        var reverse = Should.Throw<InvalidOperationException>(() =>
        {
            var options = new NotificationDataOptions();
            options.Add("Test.Second", typeof(DistinctDataA));
            options.Add("Test.First", typeof(DistinctDataA));
        });

        forward.Message.ShouldBe(reverse.Message);
        forward.Message.ShouldContain(typeof(DistinctDataA).FullName!);
        forward.Message.ShouldContain("Test.First");
        forward.Message.ShouldContain("Test.Second");
    }

    [Fact]
    public void Exact_data_mapping_repeat_is_idempotent()
    {
        var options = new NotificationDataOptions();

        options.Add<DistinctDataA>();
        options.Add<DistinctDataA>();
        options.Add("Test.DistinctA", typeof(DistinctDataA));

        options.DataTypes.Count.ShouldBe(1);
        options.DataTypes["Test.DistinctA"].ShouldBe(typeof(DistinctDataA));
    }

    [Fact]
    public void Data_types_dictionary_cannot_bypass_conflict_validation()
    {
        var discriminatorConflict = new NotificationDataOptions();
        discriminatorConflict.DataTypes["Test.Direct"] = typeof(DistinctDataA);

        Should.Throw<InvalidOperationException>(() =>
            discriminatorConflict.DataTypes["Test.Direct"] = typeof(DistinctDataB));

        var typeConflict = new NotificationDataOptions();
        typeConflict.DataTypes["Test.DirectA"] = typeof(DistinctDataA);

        Should.Throw<InvalidOperationException>(() =>
            typeConflict.DataTypes["Test.DirectB"] = typeof(DistinctDataA));
    }

    [Fact]
    public void Data_discriminator_registration_and_lookup_are_ordinal_and_case_sensitive()
    {
        var options = new NotificationDataOptions();
        options.Add<CaseSensitiveDataUpper>();
        options.Add<CaseSensitiveDataLower>();
        var registry = new NotificationDataTypeRegistry(Options.Create(options));

        registry.GetTypeOrNull("Test.Case").ShouldBe(typeof(CaseSensitiveDataUpper));
        registry.GetTypeOrNull("test.case").ShouldBe(typeof(CaseSensitiveDataLower));
        registry.GetTypeOrNull("TEST.CASE").ShouldBeNull();
        registry.GetDiscriminatorOrNull(typeof(CaseSensitiveDataUpper)).ShouldBe("Test.Case");
        registry.GetDiscriminatorOrNull(typeof(CaseSensitiveDataLower)).ShouldBe("test.case");
    }

    [Fact]
    public void Definition_names_are_ordinal_case_sensitive_and_every_exact_repeat_is_rejected()
    {
        var context = new NotificationDefinitionContext();
        context.Add(NewDefinition("Test.Definition"));
        context.Add(NewDefinition("test.definition"));

        context.GetOrNull("Test.Definition").ShouldNotBeNull();
        context.GetOrNull("test.definition").ShouldNotBeNull();
        context.GetOrNull("TEST.DEFINITION").ShouldBeNull();

        var exception = Should.Throw<InvalidOperationException>(() =>
            context.Add(NewDefinition("Test.Definition")));
        exception.Message.ShouldContain("Test.Definition");
        exception.Message.ShouldContain("<direct registration>");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Definition_name_rejects_empty_or_whitespace_values_immediately(string name)
    {
        Should.Throw<ArgumentException>(() =>
            new NotificationDefinition(name, new FixedLocalizableString("Invalid")));
    }

    [Fact]
    public void Definition_contract_records_stable_payload_and_entity_metadata()
    {
        var definition = NewDefinition("Test.Contract")
            .WithPayload<DistinctDataA>()
            .WithPayload("Test.DistinctA")
            .WithPayload<DistinctDataA>()
            .WithEntityContract(NotificationEntityRequirement.Required, "Demo.Order");

        definition.PayloadDiscriminator.ShouldBe("Test.DistinctA");
        definition.EntityRequirement.ShouldBe(NotificationEntityRequirement.Required);
        definition.ExpectedEntityTypeName.ShouldBe("Demo.Order");
    }

    [Fact]
    public void Definition_contract_rejects_conflicting_reconfiguration()
    {
        Should.Throw<InvalidOperationException>(() =>
            NewDefinition("Test.PayloadDiscriminatorConflict")
                .WithPayload<DistinctDataA>()
                .WithPayload<DistinctDataB>());
        Should.Throw<InvalidOperationException>(() =>
            NewDefinition("Test.PayloadTypeConflict")
                .WithPayload<DuplicateDataA>()
                .WithPayload<DuplicateDataB>());
        Should.Throw<ArgumentOutOfRangeException>(() =>
            NewDefinition("Test.Unspecified")
                .WithEntityContract(NotificationEntityRequirement.Unspecified));
        Should.Throw<ArgumentException>(() =>
            NewDefinition("Test.Forbidden")
                .WithEntityContract(NotificationEntityRequirement.Forbidden, "Demo.Order"));
        Should.Throw<InvalidOperationException>(() =>
            NewDefinition("Test.EntityConflict")
                .WithEntityContract(NotificationEntityRequirement.Required, "Demo.Order")
                .WithEntityContract(NotificationEntityRequirement.Optional, "Demo.Order"));
    }

    [Fact]
    public async Task Duplicate_definition_providers_fail_host_start_independent_of_provider_order()
    {
        var forward = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<DuplicateDefinitionsForwardStartupModule>());
        var reverse = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<DuplicateDefinitionsReverseStartupModule>());

        forward.Message.ShouldBe(reverse.Message);
        forward.Message.ShouldContain("Test.CrossModuleDuplicate");
        forward.Message.ShouldContain(typeof(TestProviderADefinitionProvider).FullName!);
        forward.Message.ShouldContain(typeof(TestProviderBDefinitionProvider).FullName!);
        forward.Message.ShouldContain(typeof(TestProviderADefinitionProvider).Assembly.GetName().Name!);
        forward.Message.ShouldContain(typeof(TestProviderBDefinitionProvider).Assembly.GetName().Name!);
    }

    [Fact]
    public async Task Duplicate_data_discriminators_fail_host_start_independent_of_registration_order()
    {
        var forward = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<DuplicateDataForwardStartupModule>());
        var reverse = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<DuplicateDataReverseStartupModule>());

        forward.Message.ShouldBe(reverse.Message);
        forward.Message.ShouldContain("Test.Duplicate");
        forward.Message.ShouldContain(typeof(DuplicateDataA).FullName!);
        forward.Message.ShouldContain(typeof(DuplicateDataB).FullName!);
    }

    [Fact]
    public async Task Ambiguous_discriminators_for_one_type_fail_host_start()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<AmbiguousDataTypeStartupModule>());

        exception.Message.ShouldContain(typeof(DistinctDataA).FullName!);
        exception.Message.ShouldContain("Test.First");
        exception.Message.ShouldContain("Test.Second");
    }

    [Fact]
    public async Task Distinct_registrations_and_exact_data_repeat_allow_host_start()
    {
        await StartHostAsync<ValidRegistrationsStartupModule>();
    }

    [Fact]
    public async Task Definition_referencing_unregistered_payload_discriminator_fails_host_start()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<UnregisteredPayloadContractStartupModule>());

        exception.Message.ShouldContain("Test.UnregisteredPayloadContract");
        exception.Message.ShouldContain("Test.Definition.Unregistered");
        exception.Message.ShouldContain(nameof(NotificationDataOptions));
    }

    [Fact]
    public async Task Definition_referencing_registered_payload_discriminator_allows_host_start()
    {
        await StartHostAsync<RegisteredPayloadContractStartupModule>();
    }

    [Fact]
    public async Task Type_safe_payload_contract_cannot_be_weakened_by_string_repeat_in_either_order()
    {
        var genericFirst = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<GenericThenStringPayloadContractStartupModule>());
        var stringFirst = await Should.ThrowAsync<InvalidOperationException>(
            () => StartHostAsync<StringThenGenericPayloadContractStartupModule>());

        genericFirst.Message.ShouldBe(stringFirst.Message);
        genericFirst.Message.ShouldContain("Test.OrderIndependentPayloadContract");
        genericFirst.Message.ShouldContain("Test.Definition.Unregistered");
        genericFirst.Message.ShouldContain(typeof(DefinitionContractData).FullName!);
        genericFirst.Message.ShouldContain(typeof(DefinitionContractAliasData).FullName!);
    }

    [Fact]
    public async Task Missing_upcast_step_fails_host_start_with_the_exact_gap()
    {
        var exception = await Should.ThrowAsync<Exception>(
            () => StartHostAsync<MissingUpcastStepStartupModule>());

        exception.ToString().ShouldContain("Test.EvolvingOrder");
        exception.ToString().ShouldContain("v2→v3");
    }

    [Fact]
    public async Task Duplicate_upcast_registration_fails_host_start_clearly()
    {
        var exception = await Should.ThrowAsync<Exception>(
            () => StartHostAsync<DuplicateUpcastStepStartupModule>());

        exception.ToString().ShouldContain("Duplicate notification data upcaster");
        exception.ToString().ShouldContain("Test.EvolvingOrder");
        exception.ToString().ShouldContain("v1");
    }

    [Fact]
    public async Task Complete_upcast_chain_allows_reverse_registration_order_at_host_start()
    {
        await StartHostAsync<CompleteUpcastChainStartupModule>();
    }

    [Fact]
    public async Task Invalid_distribution_batch_configuration_fails_host_start()
    {
        var exception = await Should.ThrowAsync<Exception>(
            () => StartHostAsync<InvalidDistributionBatchStartupModule>());

        exception.ToString().ShouldContain(nameof(NotificationOptions.DeliveryEventRecipientLimit));
        exception.ToString().ShouldContain(NotificationOptions.MaxDistributionBatchSize.ToString());
    }

    [Fact]
    public async Task Abstractions_only_host_registers_one_tolerant_global_converter()
    {
        using var host = BuildHost<AbstractionsOnlyStartupModule>();
        await host.StartAsync();

        var serializerOptions = host.Services
            .GetRequiredService<IOptions<AbpSystemTextJsonSerializerOptions>>()
            .Value
            .JsonSerializerOptions;

        serializerOptions.Converters
            .OfType<NotificationDataJsonConverter>()
            .Count()
            .ShouldBe(1);

        var data = JsonSerializer.Deserialize<NotificationData>(
            """{"type":"Other.Product.Payload","schemaVersion":7,"value":"opaque"}""",
            serializerOptions);

        var unsupported = data.ShouldBeOfType<UnsupportedNotificationData>();
        unsupported.Reason.ShouldBe(UnsupportedNotificationDataReason.UnknownDiscriminator);
        unsupported.OriginalDiscriminator.ShouldBe("Other.Product.Payload");
        unsupported.OriginalSchemaVersion.ShouldBe(7);

        await host.StopAsync();
    }

    [Fact]
    public async Task Definition_provider_can_resolve_options_and_manager_during_host_start()
    {
        await StartHostAsync<ProviderDependencyStartupModule>();
    }

    [Fact]
    public async Task Host_start_uses_the_registered_definition_manager_override()
    {
        await StartHostAsync<CustomDefinitionManagerStartupModule>();
    }

    [Fact]
    public async Task Convention_discovered_definition_provider_executes_once_across_startup_and_concurrent_lookups()
    {
        TestProviderADefinitionProvider.ResetDefineCallCount();
        using var host = BuildHost<SingleDefinitionProviderStartupModule>();
        host.Services.GetService<TestProviderADefinitionProvider>().ShouldNotBeNull();

        await host.StartAsync();
        var definitionManager = host.Services.GetRequiredService<INotificationDefinitionManager>();
        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => definitionManager.GetAll())));

        TestProviderADefinitionProvider.DefineCallCount.ShouldBe(1);
        await host.StopAsync();
    }

    private static NotificationDefinition NewDefinition(string name)
    {
        return new NotificationDefinition(name, new FixedLocalizableString(name));
    }

    private static async Task StartHostAsync<TStartupModule>() where TStartupModule : IAbpModule
    {
        using var host = BuildHost<TStartupModule>();
        await host.StartAsync();
        await host.StopAsync();
    }

    private static IHost BuildHost<TStartupModule>() where TStartupModule : IAbpModule
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddApplication<TStartupModule>();
        return builder.Build();
    }
}

[NotificationDataType("Test.Duplicate")]
internal sealed class DuplicateDataA : NotificationData
{
}

[NotificationDataType("Test.Duplicate")]
internal sealed class DuplicateDataB : NotificationData
{
}

[NotificationDataType("Test.DistinctA")]
internal sealed class DistinctDataA : NotificationData
{
}

[NotificationDataType("Test.DistinctB")]
internal sealed class DistinctDataB : NotificationData
{
}

[NotificationDataType("Test.Case")]
internal sealed class CaseSensitiveDataUpper : NotificationData
{
}

[NotificationDataType("test.case")]
internal sealed class CaseSensitiveDataLower : NotificationData
{
}

[NotificationDataType("Test.Definition.Unregistered")]
internal sealed class DefinitionContractData : NotificationData
{
}

[NotificationDataType("Test.Definition.Unregistered")]
internal sealed class DefinitionContractAliasData : NotificationData
{
}

internal sealed class DefinitionContractProvider : INotificationDefinitionProvider
{
    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
                "Test.UnregisteredPayloadContract",
                new FixedLocalizableString("Payload contract"))
            .WithPayload<DefinitionContractData>());
    }
}

internal sealed class ProviderDependencyDefinitionProvider : INotificationDefinitionProvider
{
    private readonly NotificationOptions _options;
    private readonly INotificationDefinitionManager _definitionManager;

    public ProviderDependencyDefinitionProvider(
        IOptions<NotificationOptions> options,
        INotificationDefinitionManager definitionManager)
    {
        _options = options.Value;
        _definitionManager = definitionManager;
    }

    public void Define(INotificationDefinitionContext context)
    {
        _options.ShouldNotBeNull();
        _definitionManager.ShouldNotBeNull();
        _options.DefinitionProviders.Count(type => type == typeof(ProviderDependencyDefinitionProvider)).ShouldBe(1);
        context.Add(new NotificationDefinition(
            "Test.ProviderDependencies",
            new FixedLocalizableString("Provider dependencies")));
    }
}

[DisableConventionalRegistration]
internal sealed class CustomNotificationDefinitionManager : NotificationDefinitionManager
{
    public CustomNotificationDefinitionManager(
        IOptions<NotificationOptions> options,
        IServiceScopeFactory serviceScopeFactory)
        : base(options, serviceScopeFactory)
    {
    }

    protected override IDictionary<string, NotificationDefinition> CreateDefinitions()
    {
        var definition = new NotificationDefinition(
            "Test.CustomManager",
            new FixedLocalizableString("Custom manager"));
        return new Dictionary<string, NotificationDefinition>(StringComparer.Ordinal)
        {
            [definition.Name] = definition
        };
    }
}

[DependsOn(typeof(TestProviderAModule), typeof(TestProviderBModule))]
public class DuplicateDefinitionsForwardStartupModule : AbpModule
{
}

[DependsOn(typeof(TestProviderBModule), typeof(TestProviderAModule))]
public class DuplicateDefinitionsReverseStartupModule : AbpModule
{
}

[DependsOn(typeof(AbpNotificationsAbstractionsModule))]
public class DuplicateDataForwardStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<DuplicateDataA>();
            options.Add<DuplicateDataB>();
        });
    }
}

[DependsOn(typeof(AbpNotificationsAbstractionsModule))]
public class DuplicateDataReverseStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<DuplicateDataB>();
            options.Add<DuplicateDataA>();
        });
    }
}

[DependsOn(typeof(AbpNotificationsAbstractionsModule))]
public class AmbiguousDataTypeStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add("Test.First", typeof(DistinctDataA));
            options.Add("Test.Second", typeof(DistinctDataA));
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class ValidRegistrationsStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<DistinctDataA>();
            options.Add<DistinctDataA>();
            options.Add<DistinctDataB>();
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class UnregisteredPayloadContractStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<DefinitionContractProvider>();
        Configure<NotificationOptions>(options =>
            options.DefinitionProviders.Add(typeof(DefinitionContractProvider)));
    }
}

internal sealed class GenericThenStringPayloadContractProvider : INotificationDefinitionProvider
{
    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
                "Test.OrderIndependentPayloadContract",
                new FixedLocalizableString("Order-independent payload contract"))
            .WithPayload<DefinitionContractData>()
            .WithPayload("Test.Definition.Unregistered"));
    }
}

internal sealed class StringThenGenericPayloadContractProvider : INotificationDefinitionProvider
{
    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
                "Test.OrderIndependentPayloadContract",
                new FixedLocalizableString("Order-independent payload contract"))
            .WithPayload("Test.Definition.Unregistered")
            .WithPayload<DefinitionContractData>());
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class RegisteredPayloadContractStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<DefinitionContractProvider>();
        Configure<NotificationOptions>(options =>
            options.DefinitionProviders.Add(typeof(DefinitionContractProvider)));
        Configure<NotificationDataOptions>(options => options.Add<DefinitionContractData>());
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class GenericThenStringPayloadContractStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<GenericThenStringPayloadContractProvider>();
        Configure<NotificationOptions>(options =>
            options.DefinitionProviders.Add(typeof(GenericThenStringPayloadContractProvider)));
        Configure<NotificationDataOptions>(options => options.Add<DefinitionContractAliasData>());
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class StringThenGenericPayloadContractStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<StringThenGenericPayloadContractProvider>();
        Configure<NotificationOptions>(options =>
            options.DefinitionProviders.Add(typeof(StringThenGenericPayloadContractProvider)));
        Configure<NotificationDataOptions>(options => options.Add<DefinitionContractAliasData>());
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class MissingUpcastStepStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<EvolvingOrderNotificationData>();
            options.AddUpcaster<EvolvingOrderNotificationData>(1, payload => payload);
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class DuplicateUpcastStepStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<EvolvingOrderNotificationData>();
            options.AddUpcaster<EvolvingOrderNotificationData>(1, payload => payload);
            options.AddUpcaster<EvolvingOrderNotificationData>(1, payload => payload);
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class CompleteUpcastChainStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<EvolvingOrderNotificationData>();
            options.AddUpcaster<EvolvingOrderNotificationData>(2, payload => payload);
            options.AddUpcaster<EvolvingOrderNotificationData>(1, payload => payload);
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class InvalidDistributionBatchStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<NotificationOptions>(options =>
            options.DeliveryEventRecipientLimit = NotificationOptions.MaxDistributionBatchSize + 1);
    }
}

[DependsOn(typeof(AbpNotificationsAbstractionsModule))]
public class AbstractionsOnlyStartupModule : AbpModule
{
}

[DependsOn(typeof(AbpNotificationsModule))]
public class ProviderDependencyStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<ProviderDependencyDefinitionProvider>();
        Configure<NotificationOptions>(options =>
            options.DefinitionProviders.Add(typeof(ProviderDependencyDefinitionProvider)));
    }
}

[DependsOn(typeof(TestProviderAModule))]
public class SingleDefinitionProviderStartupModule : AbpModule
{
}

[DependsOn(typeof(TestProviderAModule), typeof(TestProviderBModule))]
public class CustomDefinitionManagerStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(
            ServiceDescriptor.Singleton<INotificationDefinitionManager, CustomNotificationDefinitionManager>());
    }
}

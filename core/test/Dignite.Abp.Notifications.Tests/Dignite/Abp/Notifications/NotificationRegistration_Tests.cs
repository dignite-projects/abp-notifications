using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
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

    [Fact]
    public async Task Duplicate_definition_providers_fail_host_start_independent_of_provider_order()
    {
        var forward = await Should.ThrowAsync<OptionsValidationException>(
            () => StartHostAsync<DuplicateDefinitionsForwardStartupModule>());
        var reverse = await Should.ThrowAsync<OptionsValidationException>(
            () => StartHostAsync<DuplicateDefinitionsReverseStartupModule>());

        forward.Message.ShouldBe(reverse.Message);
        forward.Message.ShouldContain("Test.StartupDuplicateDefinition");
        forward.Message.ShouldContain(typeof(DuplicateDefinitionFirstProvider).FullName!);
        forward.Message.ShouldContain(typeof(DuplicateDefinitionSecondProvider).FullName!);
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

    private static NotificationDefinition NewDefinition(string name)
    {
        return new NotificationDefinition(name, new FixedLocalizableString(name));
    }

    private static async Task StartHostAsync<TStartupModule>() where TStartupModule : IAbpModule
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddApplication<TStartupModule>();

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
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

internal sealed class DuplicateDefinitionFirstProvider : INotificationDefinitionProvider
{
    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
            "Test.StartupDuplicateDefinition",
            new FixedLocalizableString("First")));
    }
}

internal sealed class DuplicateDefinitionSecondProvider : INotificationDefinitionProvider
{
    public void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
            "Test.StartupDuplicateDefinition",
            new FixedLocalizableString("Second")));
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class DuplicateDefinitionsForwardStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<DuplicateDefinitionFirstProvider>();
        context.Services.AddTransient<DuplicateDefinitionSecondProvider>();
        Configure<NotificationOptions>(options =>
        {
            options.DefinitionProviders.Add(typeof(DuplicateDefinitionFirstProvider));
            options.DefinitionProviders.Add(typeof(DuplicateDefinitionSecondProvider));
        });
    }
}

[DependsOn(typeof(AbpNotificationsModule))]
public class DuplicateDefinitionsReverseStartupModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<DuplicateDefinitionSecondProvider>();
        context.Services.AddTransient<DuplicateDefinitionFirstProvider>();
        Configure<NotificationOptions>(options =>
        {
            options.DefinitionProviders.Add(typeof(DuplicateDefinitionSecondProvider));
            options.DefinitionProviders.Add(typeof(DuplicateDefinitionFirstProvider));
        });
    }
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

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.EventBus;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpFeaturesModule),
    typeof(AbpTimingModule),
    typeof(AbpBackgroundJobsAbstractionsModule),
    typeof(AbpGuidsModule),
    typeof(AbpEventBusModule),
    typeof(AbpJsonSystemTextJsonModule)
    )]
public class AbpNotificationsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AutoAddDefinitionProviders(context.Services);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHostedService<NotificationDefinitionStartupService>();

        // Make NotificationData serialize/deserialize polymorphically (stable discriminator) everywhere ABP's
        // System.Text.Json is used — distributed event bus, HTTP API, etc. — not just via INotificationDataSerializer.
        context.Services
            .AddOptions<AbpSystemTextJsonSerializerOptions>()
            .Configure<INotificationDataTypeRegistry>((options, registry) =>
            {
                options.JsonSerializerOptions.Converters.Add(new NotificationDataJsonConverter(registry));
            });
    }

    private static void AutoAddDefinitionProviders(IServiceCollection services)
    {
        services.PostConfigure<NotificationOptions>(options =>
        {
            var definitionProviders = services
                .Where(descriptor => descriptor.ImplementationType != null &&
                                     typeof(INotificationDefinitionProvider).IsAssignableFrom(
                                         descriptor.ImplementationType))
                .Select(descriptor => descriptor.ImplementationType!)
                .Distinct()
                .ToList();

            options.DefinitionProviders.AddIfNotContains(definitionProviders);
        });
    }
}

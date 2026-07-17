using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpFeaturesModule),
    typeof(AbpTimingModule),
    typeof(AbpBackgroundJobsAbstractionsModule),
    typeof(AbpGuidsModule),
    typeof(AbpEventBusModule)
    )]
public class AbpNotificationsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AutoAddDefinitionProviders(context.Services);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services
            .AddOptions<NotificationOptions>()
            .Validate(options =>
            {
                options.ValidateDistributionBatching();
                return true;
            })
            .ValidateOnStart();

        context.Services.AddHostedService<NotificationDefinitionStartupService>();
        context.Services.AddHostedService<NotificationDeliveryRetryWorker>();

        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Handlers.Add<NotificationDeliveryWorkHandler>();
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

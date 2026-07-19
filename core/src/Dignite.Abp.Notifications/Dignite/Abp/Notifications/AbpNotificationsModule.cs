using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EventBus;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Features;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpNotificationsAbstractionsModule),
    typeof(AbpFeaturesModule),
    typeof(AbpTimingModule),
    typeof(AbpBackgroundJobsAbstractionsModule),
    typeof(AbpBackgroundWorkersModule),
    typeof(AbpDistributedLockingAbstractionsModule),
    typeof(AbpGuidsModule),
    typeof(AbpEventBusModule),
    typeof(AbpUnitOfWorkModule)
    )]
public class AbpNotificationsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AutoAddDefinitionProviders(context.Services);
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        AddValidatedOptions<NotificationDefinitionOptions>(context.Services, options => options.Validate());
        AddValidatedOptions<NotificationDistributionOptions>(context.Services, options => options.Validate());

        context.Services.AddHostedService<NotificationDefinitionStartupService>();

        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Handlers.Add<NotificationDeliveryRequestedHandler>();
        });
    }

    private static void AutoAddDefinitionProviders(IServiceCollection services)
    {
        services.PostConfigure<NotificationDefinitionOptions>(options =>
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

    private static void AddValidatedOptions<TOptions>(
        IServiceCollection services,
        Action<TOptions> validate)
        where TOptions : class
    {
        services
            .AddOptions<TOptions>()
            .Validate(options =>
            {
                validate(options);
                return true;
            })
            .ValidateOnStart();
    }
}

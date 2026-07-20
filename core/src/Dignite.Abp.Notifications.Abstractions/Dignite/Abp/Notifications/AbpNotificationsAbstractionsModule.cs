using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpLocalizationModule),
    typeof(AbpJsonSystemTextJsonModule)
    )]
public class AbpNotificationsAbstractionsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Force options materialization at startup so duplicate/ambiguous discriminator registrations fail fast
        // (the discriminator dictionary rejects conflicts as they are added).
        context.Services
            .AddOptions<NotificationDataOptions>()
            .Validate(_ => true)
            .ValidateOnStart();

        Configure<NotificationDataOptions>(options =>
        {
            options.Add<MessageNotificationData>();
            options.Add<LocalizableMessageNotificationData>();
            options.Add<UnsupportedNotificationData>();
        });

        // Abstractions is the deployment boundary shared by Core and independently hosted Notifier plugins.
        // Register here (once) so old/new distributed-event consumers get the same tolerant read behavior.
        context.Services
            .AddOptions<AbpSystemTextJsonSerializerOptions>()
            .Configure<INotificationDataTypeRegistry>((options, registry) =>
            {
                options.JsonSerializerOptions.Converters.Add(new NotificationDataJsonConverter(registry));
            });
    }
}

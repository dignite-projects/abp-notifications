using Dignite.Abp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Http.Client;
using Volo.Abp.Json.SystemTextJson;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterApplicationContractsModule),
    typeof(AbpHttpClientModule)
    )]
public class AbpNotificationCenterHttpApiClientModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClientProxies(
            typeof(AbpNotificationCenterApplicationContractsModule).Assembly);

        // Close roadmap problem A(c) on the client side: a remote .NET consumer deserializes ANY custom
        // NotificationData through the shared discriminator registry — not the reference implementation's
        // hard-coded, two-type switch.
        context.Services
            .AddOptions<AbpSystemTextJsonSerializerOptions>()
            .Configure<INotificationDataTypeRegistry>((options, registry) =>
            {
                options.JsonSerializerOptions.Converters.Add(new NotificationDataJsonConverter(registry));
            });
    }
}

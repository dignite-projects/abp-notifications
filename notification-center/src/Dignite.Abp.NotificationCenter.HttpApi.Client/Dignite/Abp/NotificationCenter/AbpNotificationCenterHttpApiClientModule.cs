using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Http.Client;
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
        // Application.Contracts depends on AbpNotificationsAbstractionsModule, which owns the single global
        // tolerant NotificationData converter for Core, remote clients, and independently hosted Notifiers.
    }
}

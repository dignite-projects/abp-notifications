using Dignite.Abp.NotificationCenter.MongoDB;
using Volo.Abp.Data;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(typeof(AbpNotificationCenterMongoDbModule))]
public class AbpNotificationCenterStandaloneMongoDbOutboxTestModule : AbpModule
{
    public static string ConnectionString { get; set; } = default!;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = ConnectionString;
        });

        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.UseNotificationCenterMongoDbOutbox();
            options.Outboxes.Configure(config => config.IsSendingEnabled = false);
            options.Inboxes.Configure(config => config.IsProcessingEnabled = false);
        });
    }
}

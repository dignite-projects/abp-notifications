using Dignite.Abp.NotificationCenter.EntityFrameworkCore;
using Dignite.Abp.Notifications;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.DistributedEvents;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;

namespace Dignite.Abp.NotificationCenter;

[DependsOn(
    typeof(AbpNotificationCenterApplicationModule),
    typeof(AbpNotificationCenterEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule),
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule)
    )]
public class NotificationCenterTestModule : AbpModule
{
    private SqliteConnection? _sqliteConnection;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureInMemorySqlite(context.Services);
        context.Services.AddAlwaysAllowAuthorization();

        // Route the distributed event bus through the transactional outbox/inbox on our DbContext, and keep
        // background workers off so a test can observe the stored outbox record deterministically.
        Configure<AbpDistributedEventBusOptions>(options =>
        {
            options.Outboxes.Configure(config => config.UseDbContext<NotificationCenterDbContext>());
            options.Inboxes.Configure(config => config.UseDbContext<NotificationCenterDbContext>());
        });
        Configure<AbpBackgroundWorkerOptions>(options => options.IsEnabled = false);

        // Register the custom test payload so the shared serializer can resolve it by discriminator.
        Configure<NotificationDataOptions>(options =>
        {
            options.Add<OrderShippedNotificationData>();
        });
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _sqliteConnection = CreateDatabaseAndGetConnection();

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.DbContextOptions.UseSqlite(_sqliteConnection!);
            });
        });
    }

    private static SqliteConnection CreateDatabaseAndGetConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<NotificationCenterDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var context = new NotificationCenterDbContext(options))
        {
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

        return connection;
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _sqliteConnection?.Dispose();
    }
}

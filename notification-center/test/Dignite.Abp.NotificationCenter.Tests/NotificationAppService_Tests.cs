namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared <see cref="INotificationAppService"/> scenarios against EF Core (in-memory Sqlite).</summary>
public class NotificationAppService_Tests : NotificationAppService_Tests<AbpNotificationCenterEntityFrameworkCoreTestModule>
{
}

namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared retention cleanup scenarios against EF Core (in-memory Sqlite).</summary>
public class NotificationRetentionManager_Tests :
    NotificationRetentionManager_Tests<AbpNotificationCenterEntityFrameworkCoreTestModule>
{
}

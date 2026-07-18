namespace Dignite.Abp.NotificationCenter;

/// <summary>Runs the shared retention cleanup scenarios against EF Core (in-memory Sqlite).</summary>
public class NotificationRetentionCleanup_Tests :
    NotificationRetentionCleanup_Tests<AbpNotificationCenterEntityFrameworkCoreTestModule>
{
}

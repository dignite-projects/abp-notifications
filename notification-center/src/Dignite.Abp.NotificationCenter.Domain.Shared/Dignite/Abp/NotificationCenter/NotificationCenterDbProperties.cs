namespace Dignite.Abp.NotificationCenter;

public static class NotificationCenterDbProperties
{
    public const string ConnectionStringName = "NotificationCenter";

    public static string DbTablePrefix { get; set; } = NotificationCenterConsts.DbTablePrefix;

    public static string? DbSchema { get; set; } = NotificationCenterConsts.DbSchema;
}

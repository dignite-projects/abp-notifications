namespace Dignite.Abp.Notifications;

public enum NotificationAudienceBroadcastStatus
{
    Enqueued = 0,

    Running = 1,

    CancellationRequested = 2,

    Completed = 3,

    Canceled = 4,

    Failed = 5
}

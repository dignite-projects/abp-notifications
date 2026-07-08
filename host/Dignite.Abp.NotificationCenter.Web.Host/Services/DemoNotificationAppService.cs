using System;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Users;

namespace Dignite.Abp.NotificationCenter.Web.Host.Services;

/// <summary>
/// Demo-only helper so you can trigger a fresh notification from the UI and watch the bell update live over
/// SignalR. Auto-exposed as POST /api/app/demo-notification/publish-test by ConventionalControllers.
/// </summary>
[Authorize]
public class DemoNotificationAppService : HostAppService
{
    private readonly INotificationPublisher _publisher;

    public DemoNotificationAppService(INotificationPublisher publisher)
    {
        _publisher = publisher;
    }

    public virtual Task PublishTestAsync()
    {
        return _publisher.PublishAsync(
            "Demo.Announcement",
            new MessageNotificationData($"Test notification at {Clock.Now:T}"),
            severity: NotificationSeverity.Info,
            userIds: new[] { CurrentUser.GetId() });
    }
}

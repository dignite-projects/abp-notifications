using System;
using Dignite.Abp.Notifications;
using Volo.Abp.Application.Dtos;

namespace Dignite.Abp.NotificationCenter;

public class GetNotificationDeliveryListInput : PagedResultRequestDto
{
    public Guid? NotificationId { get; set; }

    public Guid? UserId { get; set; }

    public string? Channel { get; set; }

    public NotificationDeliveryState? State { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }
}

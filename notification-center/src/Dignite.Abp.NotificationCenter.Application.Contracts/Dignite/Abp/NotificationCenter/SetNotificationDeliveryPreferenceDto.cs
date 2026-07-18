namespace Dignite.Abp.NotificationCenter;

public class SetNotificationDeliveryPreferenceDto
{
    public string? NotificationName { get; set; }

    public string? Channel { get; set; }

    public bool IsEnabled { get; set; }
}

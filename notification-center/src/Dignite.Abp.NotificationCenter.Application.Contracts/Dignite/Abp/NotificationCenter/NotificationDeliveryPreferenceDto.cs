namespace Dignite.Abp.NotificationCenter;

public class NotificationDeliveryPreferenceDto
{
    public string? NotificationName { get; set; }

    public string? Channel { get; set; }

    public bool IsEnabled { get; set; }
}

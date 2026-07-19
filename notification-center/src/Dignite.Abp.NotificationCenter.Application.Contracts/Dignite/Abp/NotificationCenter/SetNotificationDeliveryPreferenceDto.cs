namespace Dignite.Abp.NotificationCenter;

public class SetNotificationDeliveryPreferenceDto
{
    public string? NotificationName { get; set; }

    public string? Channel { get; set; }

    /// <summary>Whether matching delivery work should be allowed.</summary>
    public bool IsDeliveryEnabled { get; set; }
}

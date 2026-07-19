namespace Dignite.Abp.NotificationCenter;

public class NotificationDeliveryPreferenceDto
{
    public string? NotificationName { get; set; }

    public string? Channel { get; set; }

    /// <summary>Whether matching delivery work is allowed.</summary>
    public bool IsDeliveryEnabled { get; set; }
}

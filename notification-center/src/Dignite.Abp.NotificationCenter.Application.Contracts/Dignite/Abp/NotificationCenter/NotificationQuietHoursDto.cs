namespace Dignite.Abp.NotificationCenter;

public class NotificationQuietHoursDto
{
    public int StartMinute { get; set; }

    public int EndMinute { get; set; }

    public string TimeZoneId { get; set; } = default!;
}

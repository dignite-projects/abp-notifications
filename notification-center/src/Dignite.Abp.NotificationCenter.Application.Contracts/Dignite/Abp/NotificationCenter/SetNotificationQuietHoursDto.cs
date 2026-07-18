using System.ComponentModel.DataAnnotations;

namespace Dignite.Abp.NotificationCenter;

public class SetNotificationQuietHoursDto
{
    [Range(0, 1439)]
    public int StartMinute { get; set; }

    [Range(0, 1439)]
    public int EndMinute { get; set; }

    [Required]
    [StringLength(NotificationCenterConsts.MaxTimeZoneIdLength)]
    public string TimeZoneId { get; set; } = default!;
}

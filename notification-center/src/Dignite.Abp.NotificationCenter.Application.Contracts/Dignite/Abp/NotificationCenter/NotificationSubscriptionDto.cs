namespace Dignite.Abp.NotificationCenter;

public class NotificationSubscriptionDto
{
    public string NotificationName { get; set; } = default!;

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public bool IsSubscribed { get; set; }
}

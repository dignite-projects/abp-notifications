namespace Dignite.Abp.Notifications.Emailing;

/// <summary>
/// Options used when a recipient resolver does not provide a culture.
/// </summary>
public class NotificationEmailOptions
{
    /// <summary>Default BCP-47 culture used to build notification emails.</summary>
    public string DefaultCulture { get; set; } = "en";
}

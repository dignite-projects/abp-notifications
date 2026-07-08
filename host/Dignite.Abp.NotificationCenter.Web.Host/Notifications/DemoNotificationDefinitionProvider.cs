using Dignite.Abp.Notifications;
using Volo.Abp.Localization;

namespace Dignite.Abp.NotificationCenter.Web.Host.Notifications;

/// <summary>
/// Registers the demo notification types so they appear on the subscriptions page and can be published.
/// A real app defines these in whichever business module raises them (see template/app.md "Adding a feature").
/// </summary>
public class DemoNotificationDefinitionProvider : NotificationDefinitionProvider
{
    public override void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
            "Demo.OrderShipped",
            new FixedLocalizableString("Order shipped")));

        context.Add(new NotificationDefinition(
            "Demo.Announcement",
            new FixedLocalizableString("Announcement")));
    }
}

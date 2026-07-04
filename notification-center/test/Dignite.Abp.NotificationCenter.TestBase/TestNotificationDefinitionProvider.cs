using Dignite.Abp.Notifications;
using Volo.Abp.Localization;

namespace Dignite.Abp.NotificationCenter;

public class TestNotificationDefinitionProvider : NotificationDefinitionProvider
{
    public override void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition("order.shipped", new FixedLocalizableString("Order Shipped")));
    }
}

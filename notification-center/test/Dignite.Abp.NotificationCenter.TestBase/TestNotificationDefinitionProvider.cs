using Dignite.Abp.Notifications;
using Volo.Abp.Localization;

namespace Dignite.Abp.NotificationCenter;

public class TestNotificationDefinitionProvider : NotificationDefinitionProvider
{
    public const string TestChannel = "Test";

    public const string PreferenceNotification = "preference.test";

    public override void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition("order.shipped", new FixedLocalizableString("Order Shipped"))
            .WithPayload<MessageNotificationData>()
            .UseChannels(TestChannel));
        context.Add(new NotificationDefinition(PreferenceNotification, new FixedLocalizableString("Preference Test"))
            .WithPayload<MessageNotificationData>()
            .UseChannels("Email", "SignalR"));
        context.Add(new NotificationDefinition("mandatory.test", new FixedLocalizableString("Mandatory Test"))
            .WithPayload<MessageNotificationData>()
            .UseChannels("Email")
            .AsMandatory());
    }
}

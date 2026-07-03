using Volo.Abp.Localization;

namespace Dignite.Abp.Notifications;

public class TestNotificationDefinitionProvider : NotificationDefinitionProvider
{
    public const string Plain = "Test.Plain";
    public const string FeatureGated = "Test.FeatureGated";
    public const string DisabledFeatureGated = "Test.DisabledFeatureGated";
    public const string PermissionGranted = "Test.PermissionGranted";
    public const string PermissionDenied = "Test.PermissionDenied";

    public override void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(Plain, new FixedLocalizableString("Plain")));

        context.Add(new NotificationDefinition(FeatureGated, new FixedLocalizableString("Feature Gated"))
            .RequireFeature(TestFeatureDefinitionProvider.EnabledFeature));

        context.Add(new NotificationDefinition(DisabledFeatureGated, new FixedLocalizableString("Disabled Feature Gated"))
            .RequireFeature(TestFeatureDefinitionProvider.DisabledFeature));

        context.Add(new NotificationDefinition(PermissionGranted, new FixedLocalizableString("Permission Granted"))
            .RequirePermission(TestNotificationPermissionChecker.GrantedPermission));

        context.Add(new NotificationDefinition(PermissionDenied, new FixedLocalizableString("Permission Denied"))
            .RequirePermission(TestNotificationPermissionChecker.DeniedPermission));
    }
}

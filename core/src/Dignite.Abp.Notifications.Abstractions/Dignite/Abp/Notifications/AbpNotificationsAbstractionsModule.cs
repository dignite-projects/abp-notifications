using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;

namespace Dignite.Abp.Notifications;

[DependsOn(
    typeof(AbpLocalizationModule)
    )]
public class AbpNotificationsAbstractionsModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<NotificationDataOptions>().ValidateOnStart();

        Configure<NotificationDataOptions>(options =>
        {
            options.Add<MessageNotificationData>();
            options.Add<LocalizableMessageNotificationData>();
        });
    }
}

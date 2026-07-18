using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Abp.NotificationCenter;

[RemoteService(Name = "NotificationCenter")]
[Area("notification-center")]
[Route("api/notifications/preferences")]
public class NotificationDeliveryPreferencesController :
    AbpControllerBase,
    INotificationDeliveryPreferenceAppService
{
    protected INotificationDeliveryPreferenceAppService AppService { get; }

    public NotificationDeliveryPreferencesController(INotificationDeliveryPreferenceAppService appService)
    {
        AppService = appService;
    }

    [HttpGet]
    public virtual Task<ListResultDto<NotificationDeliveryPreferenceDto>> GetListAsync()
    {
        return AppService.GetListAsync();
    }

    [HttpPut]
    public virtual Task<NotificationDeliveryPreferenceDto> SetAsync(
        [FromBody] SetNotificationDeliveryPreferenceDto input)
    {
        return AppService.SetAsync(input);
    }

    [HttpDelete]
    public virtual Task DeleteAsync([FromQuery] DeleteNotificationDeliveryPreferenceDto input)
    {
        return AppService.DeleteAsync(input);
    }

    [HttpGet("quiet-hours")]
    public virtual Task<NotificationQuietHoursDto?> GetQuietHoursAsync()
    {
        return AppService.GetQuietHoursAsync();
    }

    [HttpPut("quiet-hours")]
    public virtual Task<NotificationQuietHoursDto> SetQuietHoursAsync(
        [FromBody] SetNotificationQuietHoursDto input)
    {
        return AppService.SetQuietHoursAsync(input);
    }

    [HttpDelete("quiet-hours")]
    public virtual Task DeleteQuietHoursAsync()
    {
        return AppService.DeleteQuietHoursAsync();
    }
}

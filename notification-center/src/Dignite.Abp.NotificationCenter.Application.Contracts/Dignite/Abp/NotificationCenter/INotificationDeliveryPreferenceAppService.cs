using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Current-user API for permanent delivery rules and a separate daily quiet-hours schedule.</summary>
public interface INotificationDeliveryPreferenceAppService : IApplicationService
{
    Task<ListResultDto<NotificationDeliveryPreferenceDto>> GetListAsync();

    Task<NotificationDeliveryPreferenceDto> SetAsync(SetNotificationDeliveryPreferenceDto input);

    Task DeleteAsync(DeleteNotificationDeliveryPreferenceDto input);

    Task<NotificationQuietHoursDto?> GetQuietHoursAsync();

    Task<NotificationQuietHoursDto> SetQuietHoursAsync(SetNotificationQuietHoursDto input);

    Task DeleteQuietHoursAsync();
}

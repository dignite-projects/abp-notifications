using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Current-user API for permanent per-channel delivery preferences (opt-out).</summary>
public interface INotificationDeliveryPreferenceAppService : IApplicationService
{
    Task<ListResultDto<NotificationDeliveryPreferenceDto>> GetListAsync();

    /// <summary>Creates or updates one delivery preference owned by the current user.</summary>
    Task<NotificationDeliveryPreferenceDto> SetPreferenceAsync(SetNotificationDeliveryPreferenceDto input);

    Task DeleteAsync(DeleteNotificationDeliveryPreferenceDto input);
}

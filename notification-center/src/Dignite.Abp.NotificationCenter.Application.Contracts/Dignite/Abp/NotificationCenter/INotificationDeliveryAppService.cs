using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Dignite.Abp.NotificationCenter;

/// <summary>Operator-only delivery-state query, retry, and explicit force-delivery surface.</summary>
public interface INotificationDeliveryAppService : IApplicationService
{
    Task<PagedResultDto<NotificationDeliveryDto>> GetListAsync(GetNotificationDeliveryListInput input);

    Task RetryAsync(Guid id);

    Task ForceDeliverAsync(Guid id);
}

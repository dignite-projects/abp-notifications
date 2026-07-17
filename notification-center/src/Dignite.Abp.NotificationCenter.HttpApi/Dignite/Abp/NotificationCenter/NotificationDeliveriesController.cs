using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.AspNetCore.Mvc;

namespace Dignite.Abp.NotificationCenter;

[RemoteService(Name = "NotificationCenter")]
[Area("notification-center")]
[Route("api/notifications/deliveries")]
public class NotificationDeliveriesController : AbpControllerBase, INotificationDeliveryAppService
{
    protected INotificationDeliveryAppService DeliveryAppService { get; }

    public NotificationDeliveriesController(INotificationDeliveryAppService deliveryAppService)
    {
        DeliveryAppService = deliveryAppService;
    }

    [HttpGet]
    public virtual Task<PagedResultDto<NotificationDeliveryDto>> GetListAsync(
        GetNotificationDeliveryListInput input)
    {
        return DeliveryAppService.GetListAsync(input);
    }

    [HttpPost]
    [Route("{id}/retry")]
    public virtual Task RetryAsync(Guid id)
    {
        return DeliveryAppService.RetryAsync(id);
    }
}

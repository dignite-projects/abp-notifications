using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Dignite.Abp.NotificationCenter;

[Authorize(NotificationCenterPermissions.Deliveries.Default)]
public class NotificationDeliveryAppService : ApplicationService, INotificationDeliveryAppService
{
    protected IRepository<NotificationDeliveryRecord, Guid> DeliveryRepository { get; }
    protected INotificationDeliveryStore DeliveryStore { get; }

    public NotificationDeliveryAppService(
        IRepository<NotificationDeliveryRecord, Guid> deliveryRepository,
        INotificationDeliveryStore deliveryStore)
    {
        DeliveryRepository = deliveryRepository;
        DeliveryStore = deliveryStore;
    }

    public virtual async Task<PagedResultDto<NotificationDeliveryDto>> GetListAsync(
        GetNotificationDeliveryListInput input)
    {
        var query = await DeliveryRepository.GetQueryableAsync();
        query = query.WhereIf(input.NotificationId.HasValue,
                delivery => delivery.NotificationId == input.NotificationId)
            .WhereIf(input.UserId.HasValue, delivery => delivery.UserId == input.UserId)
            .WhereIf(input.State.HasValue, delivery => delivery.State == input.State)
            .WhereIf(input.StartDate.HasValue, delivery => delivery.CreationTime >= input.StartDate)
            .WhereIf(input.EndDate.HasValue, delivery => delivery.CreationTime <= input.EndDate);
        if (!string.IsNullOrWhiteSpace(input.Channel))
        {
            var channelKey = NotificationDeliveryIdentity.NormalizeChannel(input.Channel);
            query = query.Where(delivery => delivery.ChannelKey == channelKey);
        }

        var totalCount = await AsyncExecuter.LongCountAsync(query);
        var deliveries = await AsyncExecuter.ToListAsync(query
            .OrderByDescending(delivery => delivery.CreationTime)
            .ThenBy(delivery => delivery.Id)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount));
        return new PagedResultDto<NotificationDeliveryDto>(
            totalCount,
            deliveries.Select(MapToDto).ToList());
    }

    [Authorize(NotificationCenterPermissions.Deliveries.Retry)]
    public virtual async Task RetryAsync(Guid id)
    {
        // Resolve through the tenant-filtered repository first. This prevents an operator from probing or requeueing
        // another tenant's delivery by a known id before calling the infrastructure store's explicit tenant method.
        var delivery = await DeliveryRepository.FindAsync(id);
        if (delivery == null)
        {
            throw new EntityNotFoundException(typeof(NotificationDeliveryRecord), id);
        }

        if (delivery.State == NotificationDeliveryState.Suppressed)
        {
            throw new BusinessException(NotificationCenterErrorCodes.SuppressedDeliveryCannotBeRetried)
                .WithData("DeliveryId", id);
        }

        if (!await DeliveryStore.RetryAsync(id, CurrentTenant.Id, Clock.Now))
        {
            throw new BusinessException(NotificationCenterErrorCodes.DeliveryCannotBeRetried)
                .WithData("DeliveryId", id);
        }
    }

    [Authorize(NotificationCenterPermissions.Deliveries.ForceDeliver)]
    public virtual async Task ForceDeliverAsync(Guid id)
    {
        if (await DeliveryRepository.FindAsync(id) == null)
        {
            throw new EntityNotFoundException(typeof(NotificationDeliveryRecord), id);
        }

        if (!await DeliveryStore.ForceDeliverAsync(
                id,
                CurrentTenant.Id,
                CurrentUser.GetId(),
                Clock.Now,
                NotificationDeliveryOverrideReasonCodes.OperatorForceDelivery))
        {
            throw new BusinessException(NotificationCenterErrorCodes.DeliveryCannotBeForceDelivered)
                .WithData("DeliveryId", id);
        }
    }

    protected virtual NotificationDeliveryDto MapToDto(NotificationDeliveryRecord delivery)
    {
        return new NotificationDeliveryDto
        {
            Id = delivery.Id,
            TenantId = delivery.TenantId,
            NotificationId = delivery.NotificationId,
            UserId = delivery.UserId,
            Channel = delivery.Channel,
            IdempotencyKey = delivery.IdempotencyKey,
            Intent = delivery.Intent,
            DeliveryNotBefore = delivery.DeliveryNotBefore,
            PreferenceReasonCode = delivery.PreferenceReasonCode,
            State = delivery.State,
            AttemptCount = delivery.AttemptCount,
            NextAttemptTime = delivery.NextAttemptTime,
            LastAttemptTime = delivery.LastAttemptTime,
            LeaseExpirationTime = delivery.LeaseExpirationTime,
            CompletedTime = delivery.CompletedTime,
            LastFailureCode = delivery.LastFailureCode,
            LastFailureMessage = delivery.LastFailureMessage,
            LastForceDeliveryActorId = delivery.LastForceDeliveryActorId,
            LastForceDeliveryTime = delivery.LastForceDeliveryTime,
            LastForceDeliveryPreviousState = delivery.LastForceDeliveryPreviousState,
            LastForceDeliveryReasonCode = delivery.LastForceDeliveryReasonCode,
            CreationTime = delivery.CreationTime
        };
    }
}

import type { GetNotificationDeliveryListInput, NotificationDeliveryDto } from './models';
import { RestService, Rest } from '@abp/ng.core';
import type { PagedResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class NotificationDeliveriesService {
  private restService = inject(RestService);
  apiName = 'NotificationCenter';
  

  getList = (input: GetNotificationDeliveryListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, PagedResultDto<NotificationDeliveryDto>>({
      method: 'GET',
      url: '/api/notifications/deliveries',
      params: { notificationId: input.notificationId, userId: input.userId, channel: input.channel, state: input.state, startDate: input.startDate, endDate: input.endDate, skipCount: input.skipCount, maxResultCount: input.maxResultCount },
    },
    { apiName: this.apiName,...config });
  

  retry = (id: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'POST',
      url: `/api/notifications/deliveries/${id}/retry`,
    },
    { apiName: this.apiName,...config });
}
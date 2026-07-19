import type { DeleteNotificationDeliveryPreferenceDto, NotificationDeliveryPreferenceDto, NotificationQuietHoursDto, SetNotificationDeliveryPreferenceDto, SetNotificationQuietHoursDto } from './models';
import { RestService, Rest } from '@abp/ng.core';
import type { ListResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class NotificationDeliveryPreferencesService {
  private restService = inject(RestService);
  apiName = 'NotificationCenter';
  

  delete = (input: DeleteNotificationDeliveryPreferenceDto, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'DELETE',
      url: '/api/notifications/preferences',
      params: { notificationName: input.notificationName, channel: input.channel },
    },
    { apiName: this.apiName,...config });
  

  deleteQuietHours = (config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>({
      method: 'DELETE',
      url: '/api/notifications/preferences/quiet-hours',
    },
    { apiName: this.apiName,...config });
  

  getList = (config?: Partial<Rest.Config>) =>
    this.restService.request<any, ListResultDto<NotificationDeliveryPreferenceDto>>({
      method: 'GET',
      url: '/api/notifications/preferences',
    },
    { apiName: this.apiName,...config });
  

  getQuietHours = (config?: Partial<Rest.Config>) =>
    this.restService.request<any, NotificationQuietHoursDto>({
      method: 'GET',
      url: '/api/notifications/preferences/quiet-hours',
    },
    { apiName: this.apiName,...config });
  

  setPreference = (input: SetNotificationDeliveryPreferenceDto, config?: Partial<Rest.Config>) =>
    this.restService.request<any, NotificationDeliveryPreferenceDto>({
      method: 'PUT',
      url: '/api/notifications/preferences',
      body: input,
    },
    { apiName: this.apiName,...config });
  

  setQuietHours = (input: SetNotificationQuietHoursDto, config?: Partial<Rest.Config>) =>
    this.restService.request<any, NotificationQuietHoursDto>({
      method: 'PUT',
      url: '/api/notifications/preferences/quiet-hours',
      body: input,
    },
    { apiName: this.apiName,...config });
}
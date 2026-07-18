import type {
  GetUserNotificationListInput,
  NotificationSubscriptionDto,
  NotificationSubscriptionScopeDto,
  UserNotificationDto,
} from './models';
import { RestService, Rest } from '@abp/ng.core';
import type { ListResultDto, PagedResultDto } from '@abp/ng.core';
import { Injectable, inject } from '@angular/core';
import type { UserNotificationState } from '../notifications/user-notification-state.enum';

@Injectable({
  providedIn: 'root',
})
export class NotificationsService {
  private restService = inject(RestService);
  apiName = 'NotificationCenter';

  delete = (notificationId: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'DELETE',
        url: `/api/notifications/${notificationId}`,
      },
      { apiName: this.apiName, ...config },
    );

  deleteAll = (state?: UserNotificationState, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'DELETE',
        url: '/api/notifications',
        params: { state },
      },
      { apiName: this.apiName, ...config },
    );

  getCount = (state?: UserNotificationState, config?: Partial<Rest.Config>) =>
    this.restService.request<any, number>(
      {
        method: 'GET',
        url: '/api/notifications/count',
        params: { state },
      },
      { apiName: this.apiName, ...config },
    );

  getList = (input: GetUserNotificationListInput, config?: Partial<Rest.Config>) =>
    this.restService.request<any, PagedResultDto<UserNotificationDto>>(
      {
        method: 'GET',
        url: '/api/notifications',
        params: {
          state: input.state,
          startDate: input.startDate,
          endDate: input.endDate,
          skipCount: input.skipCount,
          maxResultCount: input.maxResultCount,
        },
      },
      { apiName: this.apiName, ...config },
    );

  getSubscriptions = (config?: Partial<Rest.Config>) =>
    this.restService.request<any, ListResultDto<NotificationSubscriptionDto>>(
      {
        method: 'GET',
        url: '/api/notifications/subscriptions',
      },
      { apiName: this.apiName, ...config },
    );

  markAllAsRead = (config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'POST',
        url: '/api/notifications/mark-all-as-read',
      },
      { apiName: this.apiName, ...config },
    );

  markAsRead = (notificationId: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'POST',
        url: `/api/notifications/${notificationId}/mark-as-read`,
      },
      { apiName: this.apiName, ...config },
    );

  subscribe = (notificationName: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'POST',
        url: '/api/notifications/subscriptions',
        params: { notificationName },
      },
      { apiName: this.apiName, ...config },
    );

  subscribeScoped = (input: NotificationSubscriptionScopeDto, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'POST',
        url: '/api/notifications/subscription-scopes',
        body: input,
      },
      { apiName: this.apiName, ...config },
    );

  unsubscribe = (notificationName: string, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'DELETE',
        url: `/api/notifications/subscriptions/${notificationName}`,
      },
      { apiName: this.apiName, ...config },
    );

  unsubscribeScoped = (input: NotificationSubscriptionScopeDto, config?: Partial<Rest.Config>) =>
    this.restService.request<any, void>(
      {
        method: 'DELETE',
        url: '/api/notifications/subscription-scopes',
        params: {
          notificationName: input.notificationName,
          entityTypeName: input.entityTypeName,
          entityId: input.entityId,
        },
      },
      { apiName: this.apiName, ...config },
    );
}

import type { EntityDto, PagedResultRequestDto } from '@abp/ng.core';
import type { NotificationDeliveryIntent } from '../notifications/notification-delivery-intent.enum';
import type { NotificationDeliveryState } from '../notifications/notification-delivery-state.enum';
import type { UserNotificationState } from '../notifications/user-notification-state.enum';
import type { NotificationData } from '../notifications/models';
import type { NotificationSeverity } from '../notifications/notification-severity.enum';

export interface DeleteNotificationDeliveryPreferenceDto {
  notificationName?: string | null;
  channel?: string | null;
}

export interface GetNotificationDeliveryListInput extends PagedResultRequestDto {
  notificationId?: string | null;
  userId?: string | null;
  channel?: string | null;
  state?: NotificationDeliveryState | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface GetUserNotificationListInput extends PagedResultRequestDto {
  state?: UserNotificationState | null;
  startDate?: string | null;
  endDate?: string | null;
}

export interface NotificationDeliveryDto {
  id?: string;
  tenantId?: string | null;
  notificationId?: string;
  userId?: string;
  channel?: string;
  idempotencyKey?: string;
  intent?: NotificationDeliveryIntent;
  deliveryNotBefore?: string | null;
  preferenceReasonCode?: string | null;
  state?: NotificationDeliveryState;
  attemptCount?: number;
  nextAttemptTime?: string | null;
  lastAttemptTime?: string | null;
  leaseExpirationTime?: string | null;
  completedTime?: string | null;
  lastFailureCode?: string | null;
  lastFailureMessage?: string | null;
  creationTime?: string;
}

export interface NotificationDeliveryPreferenceDto {
  notificationName?: string | null;
  channel?: string | null;
  isEnabled?: boolean;
}

export interface NotificationQuietHoursDto {
  startMinute?: number;
  endMinute?: number;
  timeZoneId?: string;
}

export interface NotificationSubscriptionDto {
  notificationName?: string;
  entityTypeName?: string | null;
  entityId?: string | null;
  displayName?: string | null;
  description?: string | null;
  isSubscribed?: boolean;
}

export interface NotificationSubscriptionScopeDto {
  notificationName: string;
  entityTypeName?: string | null;
  entityId?: string | null;
}

export interface SetNotificationDeliveryPreferenceDto {
  notificationName?: string | null;
  channel?: string | null;
  isEnabled?: boolean;
}

export interface SetNotificationQuietHoursDto {
  startMinute?: number;
  endMinute?: number;
  timeZoneId: string;
}

export interface UserNotificationDto extends EntityDto<string> {
  userId?: string;
  notificationId?: string;
  notificationName?: string;
  notificationDisplayName?: string | null;
  data?: NotificationData | null;
  entityTypeName?: string | null;
  entityId?: string | null;
  severity?: NotificationSeverity;
  creationTime?: string;
  state?: UserNotificationState;
}

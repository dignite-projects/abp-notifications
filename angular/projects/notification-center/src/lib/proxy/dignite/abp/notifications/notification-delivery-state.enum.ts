import { mapEnumToOptions } from '@abp/ng.core';

export enum NotificationDeliveryState {
  Pending = 0,
  Processing = 1,
  Succeeded = 2,
  RetryScheduled = 3,
  Suppressed = 4,
  DeadLettered = 5,
}

export const notificationDeliveryStateOptions = mapEnumToOptions(NotificationDeliveryState);

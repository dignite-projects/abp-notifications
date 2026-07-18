import { mapEnumToOptions } from '@abp/ng.core';

export enum NotificationDeliveryState {
  Pending = 0,
  Claimed = 1,
  Succeeded = 2,
  Failed = 3,
  Suppressed = 4,
  DeadLetter = 5,
}

export const notificationDeliveryStateOptions = mapEnumToOptions(NotificationDeliveryState);

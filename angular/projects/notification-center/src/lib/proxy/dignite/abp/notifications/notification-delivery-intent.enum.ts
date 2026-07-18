import { mapEnumToOptions } from '@abp/ng.core';

export enum NotificationDeliveryIntent {
  Deliver = 0,
  Suppress = 1,
  Delay = 2,
}

export const notificationDeliveryIntentOptions = mapEnumToOptions(NotificationDeliveryIntent);

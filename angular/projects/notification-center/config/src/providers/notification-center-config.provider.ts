import { makeEnvironmentProviders } from '@angular/core';
import {
  NOTIFICATION_CENTER_REALTIME_OPTIONS,
  NotificationCenterRealtimeOptions,
} from '@dignite/abp.ng.notification-center';
import {
  NOTIFICATION_CENTER_NAV_ITEM_PROVIDERS,
  NOTIFICATION_CENTER_ROUTE_PROVIDERS,
  NOTIFICATION_CENTER_SETTING_TAB_PROVIDERS,
} from './';

export interface NotificationCenterConfigOptions {
  realtime?: NotificationCenterRealtimeOptions;
}

/**
 * Registers the Notification Center's navigation-menu contribution, toolbar bell, and Settings tab
 * into the host. A consuming app calls this once in its `app.config.ts` providers — mirroring ABP's
 * own `provideXxxConfig()` packages (e.g. `provideIdentityConfig`).
 */
export function provideNotificationCenterConfig(options: NotificationCenterConfigOptions = {}) {
  return makeEnvironmentProviders([
    NOTIFICATION_CENTER_ROUTE_PROVIDERS,
    NOTIFICATION_CENTER_NAV_ITEM_PROVIDERS,
    NOTIFICATION_CENTER_SETTING_TAB_PROVIDERS,
    ...(options.realtime
      ? [{ provide: NOTIFICATION_CENTER_REALTIME_OPTIONS, useValue: options.realtime }]
      : []),
  ]);
}

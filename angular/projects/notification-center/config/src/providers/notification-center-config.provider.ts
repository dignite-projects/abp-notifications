import { makeEnvironmentProviders } from '@angular/core';
import {
  NOTIFICATION_CENTER_NAV_ITEM_PROVIDERS,
  NOTIFICATION_CENTER_ROUTE_PROVIDERS,
  NOTIFICATION_CENTER_SETTING_TAB_PROVIDERS,
} from './';

/**
 * Registers the Notification Center's navigation-menu contribution, toolbar bell, and Settings tab
 * into the host. A consuming app calls this once in its `app.config.ts` providers — mirroring ABP's
 * own `provideXxxConfig()` packages (e.g. `provideIdentityConfig`).
 */
export function provideNotificationCenterConfig() {
  return makeEnvironmentProviders([
    NOTIFICATION_CENTER_ROUTE_PROVIDERS,
    NOTIFICATION_CENTER_NAV_ITEM_PROVIDERS,
    NOTIFICATION_CENTER_SETTING_TAB_PROVIDERS,
  ]);
}

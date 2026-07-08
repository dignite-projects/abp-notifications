import { makeEnvironmentProviders } from '@angular/core';
import { NOTIFICATION_CENTER_ROUTE_PROVIDERS } from './';

/**
 * Registers the Notification Center's navigation-menu contributions into the host. A consuming app
 * calls this once in its `app.config.ts` providers — mirroring ABP's own `provideXxxConfig()`
 * packages (e.g. `provideIdentityConfig`).
 */
export function provideNotificationCenterConfig() {
  return makeEnvironmentProviders([NOTIFICATION_CENTER_ROUTE_PROVIDERS]);
}

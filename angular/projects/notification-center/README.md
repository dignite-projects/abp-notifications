# @dignite-abp/notification-center

Angular UI library for the **Dignite ABP Notification Center** — a notification **bell** (unread
badge + dropdown) and a **subscriptions** management component, plus the ABP-generated API proxy
(`NotificationsService` + DTOs/enums) over `/api/notifications`. It mirrors the module's MVC UI for
Angular consumers and is packaged like ABP's own `@abp/ng.*` libraries: a main entry point plus a
`/config` secondary entry point.

```bash
npm install @dignite-abp/notification-center@10.0.0-rc.1
```

The npm package version stays in lockstep with the repository's NuGet package version.

## Entry points

| Import | Contents |
|---|---|
| `@dignite-abp/notification-center` | `NotificationBellComponent` (`<abp-notification-bell>`), `NotificationSubscriptionsComponent`, and the `NotificationsService` proxy + DTOs/enums. |
| `@dignite-abp/notification-center/config` | `provideNotificationCenterConfig()` — registers the navigation-menu entry into the host — plus the `eNotificationCenterRouteNames` route-name enum. Call the provider once in `app.config.ts`. |

## Usage

Register the menu contribution in the host's `app.config.ts` (mirrors `provideIdentityConfig()` etc.):

```ts
import { provideNotificationCenterConfig } from '@dignite-abp/notification-center/config';

export const appConfig: ApplicationConfig = {
  providers: [
    // ...ABP providers...
    provideNotificationCenterConfig(),
  ],
};
```

Use the subscriptions component in a page:

```ts
import { NotificationSubscriptionsComponent } from '@dignite-abp/notification-center';

@Component({
  imports: [NotificationSubscriptionsComponent],
  template: `
    <abp-notification-subscriptions />
  `,
})
export class MyNotificationsPage {}
```

The toolbar bell is registered by `provideNotificationCenterConfig()`. It loads its initial count/list
once and refreshes from the Notification Center SignalR hub (`/signalr-hubs/notifications`) on
`ReceiveNotification`; it does not poll. The hub endpoint follows ABP's SignalR convention and is mapped
server-side by ABP. Angular uses the Microsoft SignalR client, as ABP's SignalR documentation directs
non-MVC clients to do.

### Custom notification bodies and entity links

The bell keeps the notification title and time in a fixed header row. The body below it is rendered by
the notification data discriminator; register custom body components with `NotificationDataComponentsService`.

Notifications are not required to be navigable. Clicking an item marks it as read in place, updates the badge,
and keeps the item visible in the currently open dropdown. The next time the bell is opened, it reloads the
unread list and read items naturally drop out. If a resolver is registered for the item's `entityTypeName`,
the bell marks it as read and navigates through Angular Router so the SPA is not reloaded:

```ts
import { inject, provideAppInitializer } from '@angular/core';
import { NotificationEntityLinksService } from '@dignite-abp/notification-center';

provideAppInitializer(() => {
  inject(NotificationEntityLinksService).register('MyApp.Order', notification => [
    '/orders',
    notification.entityId,
  ]);
});
```

Return a Router commands array, a `UrlTree`, or an app-relative URL string. External URLs still use normal
browser navigation.

## Structure

The `config` entry follows ABP's own convention (see `@abp/ng.identity/config`):

```
config/src/
├── enums/route-names.ts   # eNotificationCenterRouteNames (menu name = localization key)
├── providers/
│   ├── route.provider.ts                     # routes
│   ├── nav-item.provider.ts                  # toolbar bell
│   ├── setting-tab.provider.ts               # subscriptions settings tab
│   └── notification-center-config.provider.ts # provideNotificationCenterConfig()
└── public-api.ts
```

## The proxy is generated — don't hand-edit

Everything under `src/lib/proxy/` is produced by `abp generate-proxy -t ng -m notification-center`
(run against a running backend). Hand-written code (components) lives **outside** `proxy/` so it
survives regeneration.

## Building

From the `angular/` workspace root:

```bash
ng build notification-center
```

This emits both entry points — `dist/notification-center` (main) and `dist/notification-center/config`.

During local development the demo app consumes this library **from source**: the TypeScript path
aliases in `angular/tsconfig.json` point at `src/public-api.ts` (and `config/src/public-api.ts`), so
you don't need to pre-build the library or set up any symlinks to see your edits — just `ng serve`
the host app.

### Publishing

Tagged repository releases build, smoke-test, and publish this package automatically. Pre-releases
use npm's `next` dist-tag and stable releases use `latest`; do not publish a different npm version
manually. For a local package inspection, run `npm pack ./dist/notification-center` from the
`angular/` workspace after building.

# @dignite-abp/notification-center

Angular UI library for the **Dignite ABP Notification Center** — a notification **bell** (unread
badge + dropdown) and a **subscriptions** management component, plus the ABP-generated API proxy
(`NotificationsService` + DTOs/enums) over `/api/notifications`. It mirrors the module's MVC UI for
Angular consumers and is packaged like ABP's own `@abp/ng.*` libraries: a main entry point plus a
`/config` secondary entry point.

## Entry points

| Import | Contents |
|---|---|
| `@dignite-abp/notification-center` | `NotificationBellComponent`, `NotificationSubscriptionsComponent`, and the `NotificationsService` proxy + DTOs/enums. |
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

Use the components in a page:

```ts
import {
  NotificationBellComponent,
  NotificationSubscriptionsComponent,
} from '@dignite-abp/notification-center';

@Component({
  imports: [NotificationBellComponent, NotificationSubscriptionsComponent],
  template: `
    <nc-notification-bell />
    <nc-notification-subscriptions />
  `,
})
export class MyNotificationsPage {}
```

The bell polls the unread count on an interval and exposes a public `refresh()` the host can call
from a SignalR `ReceiveNotification` handler for live updates.

## Structure

The `config` entry follows ABP's own convention (see `@abp/ng.identity/config`):

```
config/src/
├── enums/route-names.ts   # eNotificationCenterRouteNames (menu name = localization key)
├── providers/
│   ├── route.provider.ts                     # NOTIFICATION_CENTER_ROUTE_PROVIDERS + configureRoutes()
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

```bash
cd dist/notification-center
npm publish
```

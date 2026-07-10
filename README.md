# Dignite.Abp.Notifications

An extensible, event-driven **notification framework for the [ABP Framework](https://abp.io)**, plus
an optional **Notification Center** (persistent inbox, subscriptions, read/unread state, REST API)
with **MVC** and **Angular** UI libraries.

- **Event-driven, pluggable notifiers.** The core publishes one distributed event
  (`NotificationDeliveryEto`); each notifier (SignalR, Email, …) subscribes and relays it to its own
  channel. Channels can be added, removed, or deployed independently without touching the core.
- **Two operation modes, one framework.** Run stateless (fire-and-forget real-time push, no
  persistence) or as a full Notification Center (persistent per-user inbox, subscriptions,
  read/unread state, REST API).
- **Dual persistence.** EF Core and MongoDB, behind the same `INotificationStore` abstraction.
- **Contract-driven & headless.** Every payload carries a stable type discriminator, so any
  consumer — .NET, JS/TS, or the shipped Angular library — can deserialize and render it. The
  Notification Center is headless (REST API); UI is optional.
- **Multi-tenant & permission-aware** — ABP `IMultiTenant` throughout, with optional ABP Identity
  permission gating.

> **.NET 10 · ABP 10.5.0 · LGPL-3.0-only**

## Packages

Requirements: the **.NET 10 SDK** and an ABP **10.5.0** host application. Contract layers
multi-target `netstandard2.0;netstandard2.1;net10.0` so remote and older consumers can reference
them.

**Core framework** (`core/`):

| Package | Purpose |
|---|---|
| `Dignite.Abp.Notifications.Abstractions` | Shared contracts: `NotificationData`, `NotificationDeliveryEto`, `[NotificationDataType]`, `INotificationDefinitionProvider`, `INotificationNotifier`. Notifiers and remote clients depend on **only** this. |
| `Dignite.Abp.Notifications` | The core: definitions, the publish/distribute pipeline, the `INotificationStore` abstraction + `NullNotificationStore`. |
| `Dignite.Abp.Notifications.SignalR` | Real-time push notifier (SignalR hub at `/signalr-hubs/notifications`). |
| `Dignite.Abp.Notifications.Emailing` | Email notifier (ABP `IEmailSender`). |
| `Dignite.Abp.Notifications.Emailing.Identity` | Optional ABP Identity-backed email address resolver for the Emailing notifier. |
| `Dignite.Abp.Notifications.Identity` | Permission gating for notification definitions via ABP Identity. |

**Optional Notification Center** (`notification-center/`) — persistence + REST API + UI, depends on
Core:

| Package | Purpose |
|---|---|
| `Dignite.Abp.NotificationCenter.Domain.Shared` | Enums / constants (`NotificationSeverity`, `UserNotificationState`). |
| `Dignite.Abp.NotificationCenter.Domain` | Aggregates: `Notification`, `UserNotification`, `NotificationSubscription`. |
| `Dignite.Abp.NotificationCenter.Application` / `.Application.Contracts` | Inbox / subscription app service + DTOs. |
| `Dignite.Abp.NotificationCenter.HttpApi` | REST controller at `/api/notifications`. |
| `Dignite.Abp.NotificationCenter.HttpApi.Client` | C# client proxies for remote consumers. |
| `Dignite.Abp.NotificationCenter.EntityFrameworkCore` | `INotificationStore` on EF Core (+ `NotificationCenterDbContext`). |
| `Dignite.Abp.NotificationCenter.MongoDB` | `INotificationStore` on MongoDB. |
| `Dignite.Abp.NotificationCenter.Web` | MVC UI: notification-bell view component + subscriptions page. |
| `notification-center` (Angular, `angular/projects/`) | Angular UI: proxy service + bell & subscriptions components. |

> Core never references the Notification Center — the two trees are independently installable, and
> Core keeps working with `NullNotificationStore` alone. The `host/` (runnable ABP MVC demo) and
> `angular/` (demo Angular app) folders are **local-dev demos only**; they are not packaged or
> published.

## The two operation modes

### 1. Stateless forwarding — real-time push, no persistence

Install the core plus one or more notifiers. There is no inbox or subscription: you pass explicit
recipient `userIds`, the notifier pushes to connected clients, and nothing is stored
(`NullNotificationStore`).

```csharp
[DependsOn(
    typeof(AbpNotificationsModule),
    typeof(AbpNotificationsSignalRModule)   // real-time channel
)]
public class MyHostModule : AbpModule { }
```

### 2. Full Notification Center — inbox, subscriptions, read/unread, REST API

Also install the Notification Center plus a persistence provider (EF Core or MongoDB). This adds a
persistent per-user inbox, subscriptions, read/unread state, and the `/api/notifications` REST API.

```csharp
[DependsOn(
    typeof(AbpNotificationsSignalRModule),                     // real-time channel
    // typeof(AbpNotificationsEmailingModule),                  // optional: email channel
    // typeof(AbpNotificationsEmailingIdentityModule),          // optional: UserId -> Email via ABP Identity
    typeof(AbpNotificationsIdentityModule),                    // optional: permission gating
    typeof(AbpNotificationCenterApplicationModule),            // inbox / subscription logic
    typeof(AbpNotificationCenterHttpApiModule),                // REST API at /api/notifications
    typeof(AbpNotificationCenterEntityFrameworkCoreModule),    // persistence (or ...MongoDbModule)
    typeof(AbpNotificationCenterWebModule)                     // optional: MVC bell + subscriptions UI
)]
public class MyHostModule : AbpModule { }
```

Fold the store into your host's own `DbContext` (this repo ships no `DbMigrator` — the host owns its
migrations):

```csharp
public class MyHostDbContext : AbpDbContext<MyHostDbContext>, INotificationCenterDbContext
{
    public DbSet<Notification> Notifications { get; set; } = default!;
    public DbSet<UserNotification> UserNotifications { get; set; } = default!;
    public DbSet<NotificationSubscription> NotificationSubscriptions { get; set; } = default!;

    public MyHostDbContext(DbContextOptions<MyHostDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureNotificationCenter();   // maps the three tables (+ event outbox/inbox)
    }
}
```

Then add a migration in your host and update the database, exactly as for any other ABP module.

## Defining and publishing a notification

Most features need **no new entity** — `Notification` / `UserNotification` are generic containers for
any `NotificationData`.

**1. Define the payload** with a **stable discriminator** — never a CLR type name; this is what keeps
stored and remote JSON readable across assembly-version bumps:

```csharp
[NotificationDataType("Demo.OrderShipped")]
public class OrderShippedNotificationData : NotificationData
{
    public string OrderNumber { get; set; } = default!;
    public int ItemCount { get; set; }
}
```

> For a plain text message you don't need a subclass — use the built-in `MessageNotificationData`.

**2. Register the payload type** so it (de)serializes via its discriminator:

```csharp
Configure<NotificationDataOptions>(options =>
{
    options.Add<OrderShippedNotificationData>();
});
```

**3. Register the notification definition** through an `INotificationDefinitionProvider` — its name,
display text, optional feature/permission gating, and explicit channel routing:

```csharp
public class ShopNotificationDefinitionProvider : NotificationDefinitionProvider
{
    public override void Define(INotificationDefinitionContext context)
    {
        context.Add(new NotificationDefinition(
            "Demo.OrderShipped",
            new FixedLocalizableString("Order shipped"))
            .UseChannels(SignalRNotifier.ChannelName));
    }
}
```

**4. Publish** from your business code via `INotificationPublisher`:

```csharp
await _publisher.PublishAsync(
    "Demo.OrderShipped",
    new OrderShippedNotificationData { OrderNumber = "SO-1001", ItemCount = 3 },
    entityIdentifier: new NotificationEntityIdentifier(typeof(Order), "1001"),
    severity: NotificationSeverity.Success,
    userIds: new[] { customerId });
```

`PublishAsync` distributes small explicit fan-outs inline and larger ones via a background job (the
threshold is configurable — see [Configuration](#configuration)). In Notification Center mode, omit
`userIds` to fan out to the notification's **subscribers** instead of explicit recipients.

## Notifiers

A notifier is an `INotificationNotifier<NotificationDeliveryEto>` that relays the event to a single channel.
The generic notifier contract includes ABP's `IDistributedEventHandler<TEvent>` contract, while the
non-generic `INotificationNotifier` keeps the stable channel metadata (`Name`) available for channel
enumeration and routing.

- **SignalR** — clients connect to the hub at `/signalr-hubs/notifications` (an ABP `AbpHub`, mapped
  **automatically**; the host must *not* call `MapHub`) and receive a trimmed `NotificationDelivery`
  with the recipient list stripped, so siblings' user IDs never leak to each other.
- **Emailing** — resolves each recipient's email address and sends via ABP's `IEmailSender`. The
  base Emailing package uses `NullEmailNotificationAddressResolver`, so no messages are sent until
  the host provides an `IEmailNotificationAddressResolver`. If the host uses ABP Identity as the
  email source, install `Dignite.Abp.Notifications.Emailing.Identity` and depend on
  `AbpNotificationsEmailingIdentityModule`. The host still owns SMTP / `IEmailSender`
  configuration; this module only resolves `UserId` to an email address. Address resolvers receive
  an `EmailNotificationAddressResolveContext` with the notification and `TenantId`; local
  repository-backed resolvers can switch tenant internally, while remote/microservice-backed
  resolvers should pass that tenant explicitly across their service boundary.

**Write your own** (Web Push, FCM, SMS, Webhook, …): create a project depending on
`Dignite.Abp.Notifications.Abstractions` **only**, and handle the event:

```csharp
public class WebPushNotifier
    : INotificationNotifier<NotificationDeliveryEto>, ITransientDependency
{
    public const string ChannelName = "WebPush";
    public string Name => ChannelName;

    public async Task HandleEventAsync(NotificationDeliveryEto eventData)
    {
        // Respect channel routing, and skip when there are no recipients.
        if (!NotificationChannels.IsAllowed(eventData.Channels, Name) ||
            eventData.UserIds is not { Length: > 0 })
        {
            return;
        }

        var payload = NotificationDelivery.FromEto(eventData);   // recipient list already stripped
        // ... relay `payload` to your channel SDK, per recipient ...
    }
}
```

Use `UseChannels(...)` only for external delivery channels. If a definition omits `UseChannels(...)`,
it is NotificationCenter inbox-only: the notification is persisted for the recipient to read later,
but no SignalR, Email, Web Push, or other notifier event is published. This keeps adding another
notifier from accidentally fanning out existing notification types to a new channel:

```csharp
context.Add(new NotificationDefinition("Demo.OrderShipped", new FixedLocalizableString("Order shipped"))
    .UseChannels(SignalRNotifier.ChannelName, EmailNotifier.ChannelName));
```

For inbox-only Notification Center entries, omit `UseChannels(...)`:

```csharp
context.Add(new NotificationDefinition("Demo.AuditLog", new FixedLocalizableString("Audit log")));
```

In stateless forwarding mode (`NullNotificationStore`, no NotificationCenter installed), an inbox-only
definition has nowhere to persist; publishing it fails fast. Configure at least one external channel in
that mode.

## REST API (Notification Center)

`NotificationsController` exposes the current user's inbox and subscriptions under
`/api/notifications`:

| Method & route | Purpose |
|---|---|
| `GET /api/notifications` | List the caller's notifications (paged; filter by state / date) |
| `GET /api/notifications/count` | Notification count (optionally by state) |
| `POST /api/notifications/{id}/mark-as-read` | Mark one notification read |
| `POST /api/notifications/mark-all-as-read` | Mark all read |
| `DELETE /api/notifications/{id}` | Delete one |
| `DELETE /api/notifications` | Delete all (optionally by state) |
| `GET /api/notifications/subscriptions` | List the caller's subscriptions |
| `POST /api/notifications/subscriptions` | Subscribe to a notification name |
| `DELETE /api/notifications/subscriptions/{name}` | Unsubscribe |

Every endpoint is scoped to the authenticated caller. Use `...HttpApi.Client` for a typed C# proxy,
or the Angular `NotificationsService` proxy in the browser.

## UI libraries (optional)

- **MVC** (`Dignite.Abp.NotificationCenter.Web`): a notification-bell view component and a
  subscriptions page. Configure the hub URL and per-type rendering via `NotificationCenterWebOptions`
  — `SignalRHubUrl`, `DataViewComponents` (keyed by discriminator), and `EntityLinkResolvers`.
- **Angular** (`angular/projects/notification-center`): an ABP-generated proxy service plus bell and
  subscriptions components, built against `/api/notifications` and the SignalR hub.

## Configuration

```csharp
Configure<NotificationOptions>(options =>
{
    // Explicit recipients above this count distribute on a background job instead of inline. Default: 5.
    options.DirectDistributionUserThreshold = 10;
});

// EF Core Notification Center hosts can opt in to ABP's transactional outbox so the persisted
// notification rows and the NotificationDeliveryEto commit together.
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.UseNotificationCenterEfCoreOutbox();
});
```

## Architecture

```
Notifications.Abstractions   ── data model + NotificationDeliveryEto (the one boundary everyone shares)
        │
Notifications (Core)         ── define → publish → distribute; INotificationStore; publishes NotificationDeliveryEto
        │
   ┌────┴───────────────────┐
Notifiers                 NotificationCenter (optional)
(SignalR / Email / …)     persistence · inbox · subscriptions · REST API · UI   (EF Core / MongoDB)
```

**Publish → distribute → notify:**

1. Business code calls `INotificationPublisher.PublishAsync(...)`.
2. Small explicit fan-outs distribute inline; larger ones enqueue a `NotificationDistributionJob`.
3. The distributor resolves recipients (explicit `userIds`, or subscribers from
   `INotificationStore`), checks the definition's feature/permission availability, persists to the
   store (a no-op under `NullNotificationStore`), then publishes one `NotificationDeliveryEto` when
   external channels are configured.
4. Every installed notifier handling that event relays it to its channel — honoring channel routing
   and stripping the recipient list per user.

`NotificationDeliveryEto` is the load-bearing boundary: between core and notifiers, between monolithic and
distributed deployment, and the extension point for any new channel. Under the EF Core Notification
Center provider, hosts should opt in to ABP's transactional outbox (see [Configuration](#configuration))
so "persist the notification" + "publish the event" commit together.

> **Serialization invariant:** every `NotificationData` subclass must carry a stable
> `[NotificationDataType]` discriminator and round-trip through System.Text.Json only — never a CLR
> type name / `AssemblyQualifiedName`, never Newtonsoft. That is what keeps historical and remote
> payloads readable and lets non-.NET clients render them.

## Build & test

```bash
# Build / test everything (core + notification-center) from the one solution
dotnet build Dignite.Abp.Notifications.slnx
dotnet test  Dignite.Abp.Notifications.slnx     # starts an embedded mongod for the MongoDB provider tests

# Core only (skips the embedded mongod):
dotnet test core/test/Dignite.Abp.Notifications.Tests

# Pack for local testing (version / license come from Directory.Build.props)
dotnet pack Dignite.Abp.Notifications.slnx -c Release
```

EF Core integration tests run on in-memory Sqlite and MongoDB tests on an embedded mongod, so
`dotnet test` needs no database install and no migration step.

**Run the demo host** — a runnable ABP MVC host wiring the whole stack (SignalR + Identity + EF Core
+ MVC UI) end-to-end, with a demo notification type and a publish button:

```bash
dotnet run --project host/Dignite.Abp.NotificationCenter.Web.Host
```

The `angular/` workspace consumes the same API for the Angular demo.

## Repository layout

```
core/                 core framework (Abstractions, Notifications, Identity, Emailing, Emailing.Identity, SignalR) + tests
notification-center/  optional persistence + REST API + MVC UI + tests (EF Core & MongoDB)
angular/              Angular UI library (projects/notification-center) + demo app   ── local dev only
host/                 runnable ABP MVC demo host                                     ── local dev only
Dignite.Abp.Notifications.slnx   one solution aggregating core/ + notification-center/
```

## License

Licensed under [LGPL-3.0-only](LICENSE).

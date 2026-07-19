# Dignite.Abp.Notifications

An extensible, event-driven **notification framework for the [ABP Framework](https://abp.io)**, plus
an optional **Notification Center** (persistent inbox, subscriptions, read/unread state, REST API)
with **MVC** and **Angular** UI libraries.

- **Event-driven, pluggable notifiers.** The core publishes one stable, independently claimable
  `NotificationDeliveryRequestedEto` per recipient and channel. Channels can be added, removed, or deployed
  independently without touching the core.
- **Two operation modes, one framework.** Run Core-only with process-local delivery state and no inbox,
  or install Notification Center for durable delivery state, persistent inbox, subscriptions,
  read/unread state, REST API, and operator retry.
- **Dual persistence.** EF Core and MongoDB implement the same inbox and delivery-state abstractions.
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

### Compatibility

| Dignite release | ABP Framework | Runtime | Angular library | Angular peer range |
|---|---|---|---|---|
| `10.0.0-rc.2` | `10.5.x` (built against `10.5.0`) | .NET 10 | `10.0.0-rc.2` | Angular `^21.2.0` |

The NuGet and npm packages always use the same version. Pre-release npm packages use the `next`
dist-tag; stable releases use `latest`. npm requires every package to have a `latest` tag, so until
the first stable version exists the initial pre-release is necessarily also exposed as `latest`.

**Core framework** (`core/`):

| Package | Purpose |
|---|---|
| `Dignite.Abp.Notifications.Abstractions` | Shared contracts: `NotificationData`, `NotificationDeliveryRequestedEto`, `[NotificationDataType]`, `INotificationDefinitionProvider`, `INotificationNotifier`. Notifiers and remote clients depend on **only** this. |
| `Dignite.Abp.Notifications` | The core: definitions, the publish/distribute pipeline, the `INotificationStore` abstraction + `NullNotificationStore`. |
| `Dignite.Abp.Notifications.SignalR` | Real-time push notifier (SignalR hub at `/signalr-hubs/notifications`). |
| `Dignite.Abp.Notifications.Emailing` | Email notifier (ABP `IEmailSender`). |
| `Dignite.Abp.Notifications.Emailing.Identity` | Optional ABP Identity-backed email address resolver for the Emailing notifier. |
| `Dignite.Abp.Notifications.Identity` | Permission gating and active-user audience paging via ABP Identity. |

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

## Install

The commands below show all packages installed into one host project for clarity. In a layered ABP
solution, add each package to the matching layer and put the corresponding `[DependsOn]` entry in
that layer's module.

### Stateless forwarding

Install Core plus at least one external delivery channel. This example uses SignalR:

```bash
dotnet add path/to/MyApp.csproj package Dignite.Abp.Notifications --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.Notifications.SignalR --version 10.0.0-rc.2
```

Email is optional:

```bash
dotnet add path/to/MyApp.csproj package Dignite.Abp.Notifications.Emailing --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.Notifications.Emailing.Identity --version 10.0.0-rc.2
```

### Full Notification Center with EF Core

```bash
dotnet add path/to/MyApp.csproj package Dignite.Abp.Notifications.SignalR --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.NotificationCenter.Application --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.NotificationCenter.HttpApi --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.NotificationCenter.EntityFrameworkCore --version 10.0.0-rc.2
dotnet add path/to/MyApp.csproj package Dignite.Abp.NotificationCenter.Web --version 10.0.0-rc.2
```

`Dignite.Abp.NotificationCenter.Web` is optional. For MongoDB, replace
`Dignite.Abp.NotificationCenter.EntityFrameworkCore` with
`Dignite.Abp.NotificationCenter.MongoDB`. Permission gating and active-user audience paging through
`Dignite.Abp.Notifications.Identity` are also optional.

For an Angular host, install the version-matched UI library:

```bash
npm install @dignite/abp.ng.notification-center@10.0.0-rc.2
```

Then follow [The two operation modes](#the-two-operation-modes) for the module dependencies and
[Defining and publishing a notification](#defining-and-publishing-a-notification) for the first
end-to-end notification.

## Upgrading from legacy 3.x

`10.x` is a from-scratch rewrite of the legacy `Dignite.Abp.Notifications*` and
`Dignite.Abp.NotificationCenter*` packages, not an in-place compatible upgrade. The major version
tracks the targeted ABP Framework major and also ensures that this implementation supersedes the
legacy `3.8.2` packages on NuGet.org.

Before changing an existing application from 3.x:

1. Treat the change as a module replacement and review every package/module dependency; legacy UI
   and notifier package names do not map one-for-one to this repository.
2. Give every custom `NotificationData` type a stable `[NotificationDataType("...")]` discriminator
   and register it through `NotificationDataOptions`.
3. Generate a new host-owned database migration for the three Notification Center aggregates. This
   repository does not ship a migration or an automated legacy-data converter.
4. Plan any migration of historical notifications and subscriptions explicitly before pointing the
   new module at a production database.
5. Regenerate or replace legacy clients with the new REST/C#/Angular clients and verify custom
   rendering and entity links.

Keep the application pinned to 3.8.2 until that migration has been tested; installing 10.x over a
legacy production database without a migration plan is unsupported.

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
    public DbSet<NotificationDeliveryRecord> NotificationDeliveries { get; set; } = default!;

    public MyHostDbContext(DbContextOptions<MyHostDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureNotificationCenter();   // maps the four tables
    }
}
```

Then add a migration in your host and update the database, exactly as for any other ABP module.

### Upgrading subscription identity indexes

Subscription identity is the tuple `(TenantId, UserId, NotificationName, EntityTypeName?, EntityId?)`.
The two entity values are either both null (subscribe to every entity for that notification definition)
or both present (subscribe to exactly that entity). Notification Center persists three normalized,
non-null keys so this tuple is enforced with the same ordinal semantics in relational databases and
MongoDB: `TenantKey`, `NotificationNameKey`, and `ScopeKey`.

When upgrading an existing Notification Center database, create a host-owned migration/data migration
that:

1. adds the three keys as temporarily nullable;
2. backfills every subscription by calling `NotificationSubscriptionIdentity.GetTenantKey`,
   `GetNotificationNameKey`, and `GetScopeKey` with its existing natural values;
3. removes or repairs legacy rows with only one entity field and resolves any pre-existing duplicate
   identities before adding the unique index;
4. makes the keys required and replaces the old nullable natural-value indexes with the EF Core indexes
   from `ConfigureNotificationCenter`, or the equivalent MongoDB indexes declared by
   `NotificationCenterMongoDbContext.CreateModel`.

Do not add required keys with a shared empty-string default: existing rows would collide and the value
would not preserve ordinal identity. This repository intentionally ships no migration because the
consuming host owns its database and migration history. MongoDB consumers must likewise complete the
backfill before deploying the new unique index.

#### Delivery guarantees, batches, and partial progress

Distributing a notification writes the per-user inbox rows and publishes one `NotificationDeliveryRequestedEto` per
tenant/notification/user/channel. Those writes and outgoing event records commit together only when the host
enables ABP's transactional outbox. The process that actually hosts the selected channel consumes the work event,
atomically materializes its self-contained payload snapshot directly in a claimed lease state through an
independently committed store operation. This avoids relying on visibility of an uncommitted row from the ambient
event-inbox transaction. Processes that do not host that channel ignore the event without creating or failing
state. The EF Core writer flushes and detaches each inbox batch so its change tracker stays bounded. Atomic rollback
therefore requires an ambient
**transactional** ABP unit of work; an outbox cannot make a non-transactional unit of work atomic.

| Setup | Inbox batch behavior | Persist + publish atomic | Failure/cancellation after a completed batch |
|---|---|---|---|
| EF Core, outbox + transactional UoW | each batch is flushed and detached inside the ambient transaction | yes, within one distributor/job invocation | the transaction rolls back inbox and outbox records |
| EF Core, no outbox + transactional UoW | each batch is flushed and detached inside the ambient transaction | no | inbox writes roll back, but an already published external event may have escaped |
| EF Core, non-transactional UoW | each flushed batch is durable | no | completed inbox batches and already published events can remain |
| MongoDB, outbox + transactional UoW on a supported topology | each batch participates in the ambient MongoDB transaction | yes, within one distributor/job invocation | the transaction rolls back inbox and outbox records |
| MongoDB, no outbox or non-transactional UoW | each completed provider batch is durable | no | completed inbox batches and already published events can remain |
| Core-only channel consumer | process-local delivery state; no inbox | not durable across process exit | completed external sends remain; pending/retry state is lost on restart |

If you use the shipped `NotificationCenterDbContext`, one line enables both:

```csharp
Configure<AbpDistributedEventBusOptions>(options => options.UseNotificationCenterEfCoreOutbox());
```

If you folded the tables into your own `DbContext` as above, that extension points at the wrong
context. Implement the two marker interfaces, map the event tables, and route the outbox to your own
context instead:

```csharp
public class MyHostDbContext : AbpDbContext<MyHostDbContext>,
    INotificationCenterDbContext, IHasEventInbox, IHasEventOutbox
{
    public DbSet<IncomingEventRecord> IncomingEvents { get; set; } = default!;
    public DbSet<OutgoingEventRecord> OutgoingEvents { get; set; } = default!;
    // ...the four notification DbSets

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureEventInbox();
        builder.ConfigureEventOutbox();
        builder.ConfigureNotificationCenter();
    }
}

Configure<AbpDistributedEventBusOptions>(options =>
{
    options.Outboxes.Configure(config => config.UseDbContext<MyHostDbContext>());
    options.Inboxes.Configure(config => config.UseDbContext<MyHostDbContext>());
});
```

The shipped MongoDB context has an equivalent one-line opt-in; it configures both ABP event boxes:

```csharp
Configure<AbpDistributedEventBusOptions>(options => options.UseNotificationCenterMongoDbOutbox());
```

This opt-in registers a host-lifecycle validator that fails startup unless the active Notification Center connection
is a transaction-capable replica set running MongoDB 4.0 or later with logical sessions. It does not infer the
guarantee from topology metadata alone: the validator commits an insert-and-delete transaction spanning
`AbpEventOutbox` and `AbpEventInbox`, leaving no probe rows. Standalone servers are rejected. Sharded clusters are
diagnosed but currently rejected because this package does not run a real sharded-cluster integration suite and
therefore does not advertise an unverified guarantee. The host must also use transactional ABP units of work; do
not set `AbpUnitOfWorkDefaultOptions.TransactionBehavior` to `Disabled`. For production with more than one
application instance, configure an ABP distributed-lock provider for the outbox sender and inbox processor.

`INotificationCenterMongoDbOutboxCapabilityChecker` exposes the same diagnostic used at startup. The automatic
check runs against the host/default connection. Applications that resolve a different connection per tenant must
run the checker inside every tenant context before enabling traffic and ensure every tenant deployment satisfies
the same topology requirement; the background event-box workers also need an application-specific strategy for
visiting those databases. The one-line setup assumes the usual shared Notification Center database.

#### MongoDB upgrade, collections, indexes, and cleanup

Existing MongoDB hosts should upgrade in this order:

1. convert the deployment to a MongoDB 4.0+ replica set and verify transactions independently;
2. ensure the application role can create indexes and read/write the ABP event-box collections;
3. inspect existing `AbpEventInbox` records and collapse duplicate non-empty `MessageId` values before the new
   unique index is created (retain the processed/discarded record when one exists, otherwise the oldest pending row);
4. deploy the new provider package and add `UseNotificationCenterMongoDbOutbox()`;
5. keep notification distribution inside transactional units of work, then monitor the startup capability log and
   ABP outbox/inbox workers before enabling traffic.

Notification business data needs no backfill and no collection is renamed. The provider uses ABP's conventional `AbpEventOutbox` and
`AbpEventInbox` names and creates indexes matching the EF Core query shapes: `CreationTime` for outbox sending,
`Status + CreationTime` for inbox processing/cleanup, and `MessageId` for duplicate detection. MongoDB makes
`MessageId` unique so two concurrent check-then-insert deliveries cannot create two handler-visible records. A
losing concurrent transaction is retried by the broker; ABP's normal existence check then observes the winner.
EF Core retains ABP's conventional non-unique `MessageId` index, which is a remaining provider difference.

**Breaking for custom context implementers:** `INotificationCenterMongoDbContext` now extends ABP's
`IHasEventInbox` and `IHasEventOutbox`. A consumer-owned implementation must expose
`IMongoCollection<IncomingEventRecord> IncomingEvents` and `IMongoCollection<OutgoingEventRecord> OutgoingEvents`,
call `ConfigureEventInbox()` and `ConfigureEventOutbox()` from `CreateModel`, and add the three indexes described
above (including MongoDB's unique `MessageId` index). The non-generic
`UseNotificationCenterMongoDbOutbox()` targets the shipped `NotificationCenterMongoDbContext`; a custom context
must instead configure both boxes explicitly:

```csharp
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.Outboxes.Configure(config => config.UseMongoDbContext<MyHostMongoDbContext>());
    options.Inboxes.Configure(config => config.UseMongoDbContext<MyHostMongoDbContext>());
});
```

That custom route does not activate the shipped-context hosted validator. Run an equivalent committed transaction
probe for every resolved database during the host lifecycle before accepting traffic; merely checking `setName`,
logical sessions, or wire version is not sufficient.

Processed/discarded inbox records are retained for ABP's deduplication window and then removed by ABP's built-in
cleanup worker. Tune `AbpEventBusBoxesOptions.WaitTimeToDeleteProcessedInboxEvents` and
`CleanOldEventTimeIntervalSpan` for the host's redelivery window and storage budget. Outbox rows are deleted after
successful broker publication. Operational TTL indexes are not created because they can bypass ABP's status-aware
cleanup semantics.

EF Core requires host-owned schema migrations for `AbpEventOutbox`/`AbpEventInbox`; MongoDB creates collections
and indexes through its model initialization instead. Both providers use ABP's dispatcher/inbox terminology and
require a transactional unit of work for atomic persist-and-record. The MongoDB-specific unique inbox index and
replica-set startup probe are the remaining reliability/configuration differences. Neither provider claims
exactly-once external delivery or atomicity across all independently scheduled fan-out jobs.

Without the opt-in, a crash between inbox persistence and work-event publication can leave a notification with no
channel delivery. Cancellation remains a boundary for stopping new work, not compensation for completed work.

#### Delivery reliability, retries, and external side effects

`NotificationDeliveryRequestedEto` contains exactly one recipient and one channel. Its `DeliveryId` and
`IdempotencyKey` are deterministic from the tenant/host boundary, notification, user, and normalized channel.
Workers atomically claim a time-limited lease before invoking a notifier; a competing event or worker cannot claim
the same work concurrently. Expired leases are recoverable, failures use bounded exponential backoff with jitter,
and exhausted work becomes `DeadLettered`. Intentional channel decisions such as a missing email address return
`Suppressed` and are not retried automatically. Ordinary operator retry is limited to `RetryScheduled` and `DeadLettered`
work and preserves the producer-resolved intent, delay, and preference reason. A suppressed delivery can only be
requeued through the separately authorized force-delivery operation, which explicitly changes the intent to
`Deliver` and records the actor, time, previous state, and stable `operator-force-delivery` audit reason without
copying payload data into the audit fields. Each channel consumer owns this execution state. In a monolith the one
Notification Center database contains every channel; independently deployed channel services may use their own
Notification Center database because the delivery row stores a stable System.Text.Json payload snapshot and does
not require the producer's `Notification` row to retry. Operational queries therefore report the channels hosted
by that application/database.

The guarantee is **at-least-once scheduling with a durable internal idempotency boundary**, not exactly-once
delivery to an external provider. A process can fail after an email, webhook, or push provider accepted the side
effect but before `Succeeded` was stored. Forward `workItem.IdempotencyKey` to providers that offer an idempotency
or deduplication key; without provider support, a retry may repeat that external side effect. Diagnostic fields use
fixed reason codes and sanitized messages and never contain exception text, addresses, recipient IDs, or payloads;
the separate payload snapshot follows the normal stable-discriminator/System.Text.Json persistence rules.

Core-only applications continue to work without Notification Center. `InMemoryNotificationDeliveryStore` keeps the
same state machine and concurrent-claim behavior in process, but restart loses pending work, leases, retry history,
and operator visibility. Install either Notification Center persistence provider when retries must survive a crash
or deployment.

#### Retention and lifecycle cleanup

Notification Center retention is opt-in. The hosted cleanup worker is disabled by default, so upgrading preserves
the historical behavior of retaining inbox rows, base payload rows, and delivery state until an application or user
explicitly deletes them. Enable it only after setting retention windows that match your operational and legal
requirements:

```csharp
Configure<NotificationRetentionOptions>(options =>
{
    options.IsCleanupEnabled = true;
    options.CleanupBatchSize = 500;              // max scanned candidates per record kind and pass
    options.CleanupWorkerPeriod = TimeSpan.FromHours(6);
    options.ReadUserNotificationRetention = TimeSpan.FromDays(180);
    options.TerminalDeliveryRetention = TimeSpan.FromDays(30);
    options.OrphanNotificationRetention = TimeSpan.FromDays(30);
    options.NotificationDeletionQuarantineDuration = TimeSpan.FromMinutes(5);
});
```

The same service can be called manually for dry-run/reporting:

```csharp
var report = await retentionCleanup.CleanupAsync(new NotificationRetentionCleanupRequest
{
    IsDryRun = true,
    Now = clock.Now
});
```

`NotificationRetentionCleanupResult` reports scanned, deleted, skipped, and error counts per record kind plus the
oldest retained `Notification`, `UserNotification`, and `NotificationDeliveryRecord` creation timestamps. The same
counts are emitted through the `Dignite.Abp.NotificationCenter.Retention` meter with `record_kind` and `dry_run`
tags, and the oldest retained timestamps are exposed as Unix-time-millisecond gauges. In a dry run, the deleted
counts mean "would delete"; no row is physically removed.

Retention ownership and deletion rules:

| Record | Owner | Deletion rule |
|---|---|---|
| `UserNotification` inbox row | Notification Center / current user | User actions can delete any of their own rows. Retention cleanup only deletes `Read` rows older than `ReadUserNotificationRetention`; `Unread` rows are always retained. |
| `Notification` base payload | Notification Center retention cleanup | First marked with `RetentionDeletionTime` after `OrphanNotificationRetention` and only when no same-tenant inbox row and no same-tenant delivery record still references it. Physical deletion happens after `NotificationDeletionQuarantineDuration` and a second reference check. New same-tenant inbox/delivery materialization cancels the marker; a cross-tenant row never retains or deletes another tenant's payload. |
| `NotificationDeliveryRecord` | Channel consumer / Notification Center | Cleanup deletes only terminal `Succeeded`, `Suppressed`, or `DeadLettered` rows older than `TerminalDeliveryRetention`. `Pending`, retryable `RetryScheduled`, and leased `Processing` rows are active work and are never time-deleted. |
| `NotificationSubscription` | User subscription settings | Not time-based. Delete only by the exact subscription identity through the subscription APIs. |
| `NotificationDeliveryPreference` / `NotificationQuietHours` | User delivery settings | Not time-based. Delete only by user/settings APIs; absence means default allow/no quiet hours. |
| `NotificationRetentionCleanupCursor` | Notification Center retention cleanup | Internal scan state. One cursor per cleanup scope and record kind records the last keyset position so bounded runs can resume after retained, vetoed, or failing prefixes. |
| ABP event inbox/outbox records | ABP distributed event bus | Use ABP's status-aware event-box cleanup windows. Do not add TTL deletes that bypass processed/in-progress state. |

Applications can implement `INotificationRetentionDeletionContributor` to archive a candidate or veto deletion
before a physical delete. If a contributor throws, cleanup records an error for that row, leaves the row intact,
and continues with the next candidate. Cleanup reads candidates in keyset order, caps each pass at
`CleanupBatchSize` scanned candidates per record kind, and persists the last scan position in
`NotificationRetentionCleanupCursor`, so protected, vetoed, or temporarily failing old rows do not starve later
eligible rows. Base notification deletion checks references again after contributors run, and its concurrency stamp
prevents physical deletion from winning over a same-tenant retained reference that cancels the marker concurrently.

**Retention database upgrade:** EF Core hosts should add a host-owned migration for the new retention query indexes
from `ConfigureNotificationCenter()`: `AbpNotifications.RetentionDeletionTime` and its concurrency stamp, old
payload scans (`CreationTime`, `TenantId + CreationTime`, `TenantId + RetentionDeletionTime + CreationTime`), old
read inbox scans and payload-reference checks (`State + CreationTime`, `TenantId + State + CreationTime`,
`TenantId + NotificationId`), terminal delivery scans/reference checks (`State + CompletedTime`,
`TenantKey + State + CompletedTime`, `TenantKey + NotificationId`), and the
`AbpNotificationRetentionCleanupCursors` table with its unique `IsTenantScoped + TenantKey + RecordKind` cursor
index. MongoDB contexts create the equivalent indexes and cursor collection from
`NotificationCenterMongoDbContext.CreateModel`; custom MongoDB contexts must mirror them. Existing notifications
require no backfill: a null `RetentionDeletionTime` means "not marked", and missing cleanup cursors are created on
the first non-dry-run cleanup pass. Take a normal database backup before first enabling destructive cleanup and
verify restore procedures against both the notification tables/collections, cleanup cursor state, and ABP event-box
collections.

#### Per-user delivery preferences and quiet hours

Delivery consent is independent from subscriptions and address resolution. Subscriptions decide who is a candidate
only when `userIds` is omitted; explicit and subscription-derived candidates then pass through the same
`INotificationDeliveryPreferenceEvaluator`. An email opt-out therefore does not remove the Notification Center inbox
row, suppress SignalR, or claim that an email address is missing. Core-only applications use
`AllowAllNotificationDeliveryPreferenceEvaluator`, whose deterministic default is immediate delivery.

Notification Center persists allow/deny rules at four nullable scopes. The first matching rule wins:

| Precedence | Notification | Channel |
|---:|---|---|
| 1 | exact | exact |
| 2 | exact | any |
| 3 | any | exact |
| 4 | any | any |
| 5 | no row | default allow |

Entity-specific preferences are intentionally not supported: entity interest remains a subscription concern. A
more-specific `IsEnabled = true` rule can override a broader opt-out. Quiet hours are a separate per-user daily
window (`StartMinute` inclusive, `EndMinute` exclusive) and a system time-zone identifier; equal start/end values
are rejected instead of meaning “all day.” Normal work created during the window is delayed, not dropped. The
producer writes `Delay + DeliveryNotBefore` into the single-user/channel `NotificationDeliveryRequestedEto`; its owning
remote consumer persists the work as pending, and the delivery retry worker republishes it when due. Keep
`IsDeliveryRetryWorkerEnabled` enabled when using quiet hours. DST-invalid local end times advance to the first
valid minute according to the configured system time-zone rules.

Use `.AsMandatory()` on a notification definition only for system messages that must bypass permanent opt-outs and
quiet hours. Mandatory does not bypass permission/feature recipient eligibility and does not change subscription
candidate selection. The producer always resolves the final `Deliver`, `Suppress`, or `Delay` intent before the
distributed event leaves its process; remote notifiers must not query a local preference database.

**Preference database upgrade:** EF Core hosts must add `AbpNotificationDeliveryPreferences` and
`AbpNotificationQuietHours`, their tenant/user scope indexes, and the `Intent`, `DeliveryNotBefore`, and
`PreferenceReasonCode` columns on `AbpNotificationDeliveries`. Existing delivery rows initialize `Intent` to
`Deliver`; preference/quiet-hours tables require no backfill because absence means allow/no quiet hours. Custom
`INotificationCenterDbContext` implementations must expose both new `DbSet` properties. MongoDB contexts must expose
the equivalent collections and create the same unique scope indexes; existing delivery documents deserialize the
additive intent as `Deliver`.

This additive wire shape still requires a consumer-first rollout for preference enforcement. An old consumer ignores
the new intent fields and could send work that a new producer marked suppressed or delayed. Quiesce publication,
drain old work, apply the consumer schema and code, upgrade producers, and only then let users create enforced
preferences. Do not enable preference-producing code during a mixed-version window.

**Database upgrade:** this repository does not ship migrations because the consuming host owns its schema history.
Before enabling durable channel consumers, EF Core hosts must add the mapped `AbpNotificationDeliveries` table and its unique
`TenantKey + NotificationId + UserId + ChannelKey` index plus the configured due-work indexes. Custom
`INotificationCenterDbContext` implementations must expose the
`DbSet<NotificationDeliveryRecord> NotificationDeliveries` property; `ConfigureNotificationCenter()` creates the
same unique/due indexes. This is a new ledger,
so historical notifications require no backfill. Custom MongoDB contexts must expose
`IMongoCollection<NotificationDeliveryRecord> NotificationDeliveries` and configure the collection name plus the
same unique and due-work indexes in `CreateModel`.

Hosts upgrading an existing delivery ledger must also add the nullable
`LastForceDeliveryActorId`, `LastForceDeliveryTime`, `LastForceDeliveryPreviousState`, and
`LastForceDeliveryReasonCode` columns. No backfill is required. Custom `INotificationDeliveryStore`
implementations must replace the old broad `RequeueAsync` operation with preference-preserving `RetryAsync` and
the separately audited `ForceDeliverAsync` operation.

This wire change does **not** support a zero-downtime mixed-version rollout. The legacy aggregate event and generic
notifier adapter were removed before 10.0.0 stable. Quiesce notification publication, drain every queued
`Dignite.Abp.Notifications.NotificationDelivery` aggregate event, upgrade custom notifiers and consumer code/schema,
then upgrade producers and resume. A 10.0 consumer handles only the single-recipient
`NotificationDeliveryRequestedEto`; historical notifications require no ledger backfill.

Large explicit fan-outs are split into independently scheduled jobs. The publisher prepares the shared
`Notification` record first, then each job commits or fails independently; no provider promises one transaction
across the complete logical fan-out. A failed job therefore leaves partial notification-wide progress even when
each successful EF/outbox job was internally atomic. Likewise, a host/job-store failure after preparation or after
only some batches were enqueued can leave a notification record with no inbox rows or an incomplete set of queued
batches; this release does not add the resumable fan-out ledger needed to reconcile that state.

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

Discriminators use ordinal, case-sensitive comparison. Registering the same discriminator for two CLR
types, or the same CLR type under two discriminators, fails during application startup with both sides
named in the error. Repeating the exact same discriminator/type pair is safe and idempotent.

### Evolving a persisted payload

Every new write carries both the stable `type` and an explicit integer `schemaVersion`. Existing JSON
without `schemaVersion` is defined as legacy **v1**, so current v1 payload declarations need no change.
When a breaking JSON-shape change is necessary, keep the discriminator, advance the version on the
attribute, and register every consecutive JSON upcast step:

```csharp
[NotificationDataType("Demo.OrderShipped", 3)]
public class OrderShippedNotificationData : NotificationData
{
    public string OrderId { get; set; } = default!;
    public int Quantity { get; set; }
}

Configure<NotificationDataOptions>(options =>
{
    options.Add<OrderShippedNotificationData>();
    options.AddUpcaster<OrderShippedNotificationData>(1, payload =>
    {
        payload["orderId"] = payload["orderNumber"]?.DeepClone();
        payload.Remove("orderNumber");
        return payload;
    });
    options.AddUpcaster<OrderShippedNotificationData>(2, payload =>
    {
        payload["quantity"] = payload["itemCount"]?.DeepClone();
        payload.Remove("itemCount");
        return payload;
    });
});
```

An upcaster transforms payload members from N to N+1; the framework owns the reserved `type` and
`schemaVersion` envelope. Registration order does not affect execution order. Duplicate steps, a step
beyond the declared current version, or any missing v1→current link fails at application startup. Upcasting
is lazy: persisted rows are not rewritten and no EF Core/MongoDB migration is required.

`INotificationDataSerializer.Deserialize` remains strict for trusted boundaries and throws a typed
`NotificationDataReadException`. Its `Reason` distinguishes an unknown discriminator, unsupported future
version, malformed known payload, and failed upcast. Notification Center inbox reads, distributed-event
deserialization, and HTTP server/client converters use tolerant mode: they return
`UnsupportedNotificationData`, preserving the original discriminator, version, and escaped raw JSON without
activating an arbitrary CLR type. One bad historical row therefore cannot fail the rest of an inbox page.
The MVC and Angular libraries render this known placeholder as a generic unsupported-notification message and
do not display its raw diagnostic JSON.

Rolling-upgrade expectations are explicit:

| Producer | Consumer | Result |
|---|---|---|
| versionless/v1 producer | newer consumer with a complete upcast chain | deterministically upcast to the current CLR model |
| newer producer | older **schema-aware** tolerant consumer | `UnsupportedNotificationData`; processing/page continues |
| newer producer | pre-versioning consumer | not guaranteed; it may misread or reject the new shape |

Deploy schema-aware readers (including notifier/event consumers) before enabling producers that emit a newer
schema. Upcasters only help newer consumers read older data; they cannot make an already-deployed legacy
consumer understand a future breaking shape.

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
            .WithPayload<OrderShippedNotificationData>()
            .WithEntityContract(NotificationEntityRequirement.Required, "Demo.Order")
            .UseChannels(SignalRNotifier.ChannelName));
    }
}
```

Definition names also use ordinal, case-sensitive comparison. Every duplicate name is a startup error,
and the error identifies both provider types; an equivalent-looking second definition is not treated as
idempotent because definitions are mutable after construction. Provider types are convention-discovered
across modules; registering the same provider type more than once is idempotent and the provider executes once.

Definitions can opt into publish-time contracts independently for payload and entity identity:

| Declaration | Publish-time rule |
|---|---|
| no `WithPayload(...)` | Legacy compatibility: no definition-level payload check; normal serializer registration still applies |
| `WithPayload<TData>()` | A payload is required and its registered stable discriminator must exactly match `TData` |
| no `WithEntityContract(...)` | Legacy compatibility: an entity identity may be present or absent |
| `Forbidden` | Entity identity must be absent |
| `Optional` | Entity identity may be absent; when present, its type must match the optional stable type constraint |
| `Required` | Entity identity must be present and must match the optional stable type constraint |

`WithPayload<TData>()` reads `TData`'s `[NotificationDataType]` value; it never stores a CLR type name on a
wire or persistence contract. Host startup fails if that discriminator is not registered in
`NotificationDataOptions` or maps to a different CLR type. A string overload is available when a module knows
only the stable discriminator. Entity type constraints such as `"Demo.Order"` are likewise caller-chosen stable
names and compare ordinally/case-sensitively—they are never converted to or from a CLR `Type`. Repeating the
same contract is idempotent; conflicting repetitions fail immediately, and a same-discriminator string call
cannot erase the CLR registration check established by `WithPayload<TData>()`.

The publisher validates opted-in contracts before creating durable notification work or enqueueing a background
job, and the distributor validates again before writing an inbox row or publishing an external event. This
second boundary also protects direct distributor calls and replayed jobs. The trusted-recipient eligibility
bypass does not bypass payload/entity contracts. An explicitly empty `userIds` array remains a true no-op and
returns before definition resolution. Existing definitions are unchanged until they opt into either contract,
so applications can migrate definition by definition: register the payload first, add `WithPayload<TData>()`,
then declare the entity requirement that matches existing publisher call sites.

**4. Publish** from your business code via `INotificationPublisher`:

```csharp
await _publisher.PublishAsync(
    "Demo.OrderShipped",
    new OrderShippedNotificationData { OrderNumber = "SO-1001", ItemCount = 3 },
    entityIdentifier: new NotificationEntityIdentifier("Demo.Order", "1001"),
    severity: NotificationSeverity.Success,
    userIds: new[] { customerId });
```

`PublishAsync` distributes small explicit fan-outs inline and larger ones via a background job (the
threshold is configurable — see [Configuration](#configuration)). Recipient semantics are deliberate:
`userIds: null` resolves **subscribers**, an empty array is an intentional no-op, and a non-empty array
targets those users explicitly. Duplicate explicit IDs are removed before the threshold is evaluated,
inbox rows are persisted, or channel delivery is published.

### Recipient eligibility policy

A definition's permission and feature requirements govern both who may subscribe **and who may receive**
the notification. Distribution applies one `INotificationRecipientEligibilityEvaluator` to both
subscription-derived and explicitly targeted candidates. Caller-supplied exclusions are removed first;
each remaining candidate must satisfy `PermissionName` and `FeatureName` (configured through
`RequireFeature(...)`), or is filtered without an
inbox row or channel event. This is a delivery policy, not publisher authorization: publishing code still
needs its own application permission checks where appropriate.

Eligibility is evaluated in the notification's recorded `TenantId`, not whichever tenant happens to be
ambient when an inline call or background job executes. A tenant notification therefore uses that tenant's
feature values and permission context, while a host notification is evaluated in the host context. Filtered
counts and batch progress are logged without logging the recipient IDs. Replace
`INotificationRecipientEligibilityEvaluator` when a deployment can batch these lookups more efficiently;
the replacement must preserve the notification tenant/host boundary and return the same eligible/excluded
partition.

### Bounded recipient pipeline

Inline versus background selection controls where distribution runs; it does not change the amount of work in
one pipeline unit. Both explicit and subscription-derived recipients now flow through the same configurable,
bounded stages:

1. normalize one candidate page (the built-in stores use a database-side distinct/order/keyset query);
2. remove caller exclusions and evaluate definition eligibility for that page;
3. write inbox rows in bounded multi-insert groups;
4. publish work events in bounded scheduling groups, one for every eligible recipient/channel pair; the process
   hosting that channel persists and claims its consumer-owned delivery state.

The defaults are 256 candidates, 256 inbox rows, and 100 recipients converted to work records per scheduling
operation. Each value must be between 1 and `NotificationOptions.MaxDistributionBatchSize` (10,000), and invalid
configuration fails host startup. `DeliveryEventRecipientLimit` retains its existing name for configuration
compatibility, but `NotificationDeliveryRequestedEto` always carries one recipient and channel. Notification data still
has to fit the chosen transport's message-size limit.

Stable keyset paging and bounded inbox multi-inserts are canonical `INotificationStore` members. The built-in
`NotificationStore` supplies equivalent EF Core and MongoDB behavior; `NullNotificationStore` implements the same
contract without persistence. All store operations accept a cancellation token. Cancellation is observed while
scanning explicit normalization windows and between candidate, persistence, and delivery batches, not during a
provider operation already in flight.

For explicit arrays above `DirectDistributionUserThreshold`, the built-in publisher removes exclusions, prepares
the notification once, and enqueues `RecipientBatchSize` recipients per job through the canonical
`INotificationDistributor.DistributePreparedAsync` boundary; no job payload contains the complete fan-out. The public `Guid[]`
boundary still means the caller supplies the explicit input in memory. The built-in path repeatedly scans that
caller-owned array with an exclusive GUID cursor and retains at most one `RecipientBatchSize` sorted window, so
exact cross-batch duplicate removal creates no notification-wide collection. This intentionally trades additional
CPU scans and GUID-ordered large batches for a hard memory bound; recipient order is not a delivery contract. Prefer
subscription-driven resolution for very large audiences already modeled as subscriptions.
`DirectDistributionUserThreshold` is capped by the same 10,000 hard safeguard as batch sizes so inline normalization
is also bounded. Subscription scans use an exclusive user-ID keyset cursor rather than offset paging, so
inserts/deletes before the cursor cannot repeat or skip later recipients.

For tenant-wide audiences that should be resolved by infrastructure rather than by loading a `Guid[]` in the
publisher, use `INotificationAudienceBroadcaster`. A tenant broadcast is always created with an explicit
`TenantId` (or `null` for host users) and an audience name; `Guid.Empty` is rejected. Host-wide broadcasts take an
explicit tenant-id list and enqueue one tenant job at a time inside independent ABP units of work, recording
success/failure per tenant without combining tenants in one notification transaction or delivery event.

```csharp
await _audienceBroadcaster.EnqueueTenantBroadcastAsync(
    new NotificationAudienceTenantBroadcastRequest(tenantId, "Demo.TenantAnnouncement")
    {
        Data = new MessageNotificationData("Maintenance starts at 22:00 UTC.")
    });
```

The built-in audience name is `NotificationAudienceNames.AllActiveUsers`. Core defines only the abstraction and
continues to work with `NullNotificationStore`; it has no dependency on ABP Identity or Notification Center.
Installing `Dignite.Abp.Notifications.Identity` registers an Identity-backed source for that audience. It pages
ABP Identity users by an exclusive user-id keyset cursor and includes only users in the requested tenant that are
`IsActive`, not `Leaved`, and not soft-deleted. Every page is then passed to
`INotificationDistributor.DistributePreparedAsync`, so the normal feature/permission eligibility evaluator,
Notification Center inbox persistence, delivery
preferences/quiet hours, and work-event scheduling still run. Progress is represented by the stable notification
id, tenant id, page index, and cursor in job args/logs, and low-cardinality page/candidate/failure counters are
emitted from the `Dignite.Abp.Notifications.AudienceBroadcast` meter. Retried pages are idempotent against the
Notification Center `(UserId, NotificationId)` inbox identity.

`INotificationAudienceBroadcaster.GetTenantBroadcastProgressAsync(...)` returns the current observable state for a
tenant/host broadcast, and `CancelTenantBroadcastAsync(...)` records a cancellation request. The default progress
store is process-local and suitable for Core-only diagnostics; replace `INotificationAudienceBroadcastProgressStore`
when cancellation/progress must survive process restarts or be queried by another service. A job checks the store
before loading a page and before enqueueing the next cursor, then marks the broadcast completed, canceled, or
failed.

The EF Core package replaces the provider-neutral inbox writer with a flush-and-detach implementation. It saves
only the configured write group and immediately detaches those `UserNotification` entities; a regression test
asserts that 513 recipients leave zero inbox entities in the change tracker before the transactional UoW commits.

The public `Dignite.Abp.Notifications` meter exposes counters for candidates, eligible recipients, filtered
recipients, batches, and failures, plus a distribution-duration histogram. Stable instrument names are constants
on `NotificationDistributionMetrics`. Logs include the notification ID for correlating independently scheduled
batches but never recipient IDs. Low-cardinality metric tags identify recipient source, host/tenant scope, stage,
and outcome; `notification.name` is also included and should be allow-listed or aggregated when definitions are
dynamically named.

The default-size rationale and reproducible 2,001-recipient EF Core/MongoDB measurements are recorded in
[`benchmarks/issue-60-recipient-batching.md`](benchmarks/issue-60-recipient-batching.md).

`INotificationPublisher` records `CurrentTenant.Id` automatically. Code that calls `INotificationDistributor`
directly must populate `NotificationInfo.TenantId` for tenant notifications. That value is authoritative for
subscription lookup, eligibility, inbox persistence, and event/outbox publication; `null` explicitly means
**host**, even when the direct caller currently has an ambient tenant. Direct callers must also populate
`NotificationInfo.EntityTypeName` and `EntityId` together or leave both null when the definition has opted into
an entity contract; contract validation rejects a partial raw entity identity before any store or event side effect.

Trusted infrastructure has a deliberately conspicuous, explicit-recipient-only escape hatch:

```csharp
await _publisher.PublishToExplicitRecipientsWithoutEligibilityChecksAsync(
    "Demo.MandatorySecurityNotice",
    new[] { userId },
    new MessageNotificationData("Your recovery settings changed."));
```

This API never resolves subscribers, emits a Warning log for every invocation that reaches eligibility
evaluation, and bypasses both the permission and feature requirements. Use it only when receiving the
notification is itself mandatory regardless of those requirements; it is not a shortcut around a denied
recipient.

> **Upgrade note:** before this policy was introduced, explicitly supplied `userIds` implicitly bypassed
> definition requirements. `PublishAsync` now filters explicit recipients exactly like subscribers. Move
> only genuine trusted-system call sites to the named bypass API. Custom `INotificationPublisher` and
> `INotificationDistributor` implementations must add the new bypass members; custom recipient evaluation
> should replace `INotificationRecipientEligibilityEvaluator`. Direct distributor callers that previously
> omitted `NotificationInfo.TenantId` while relying on an ambient tenant must now set it explicitly; an omitted
> value no longer falls back to ambient state and is treated as a host notification.

## Notifiers

A notifier implements the single canonical `INotificationNotifier` contract and relays one claimed
`NotificationDeliveryRequestedEto` to a single channel. `Name` is the stable routing key. `DeliverAsync` receives a
`CancellationToken` and returns `Succeeded` or an intentional `Suppressed(reasonCode)` result; throwing reports a
retryable channel failure. The Core-owned distributed-event handler is the transport adapter, so a channel plugin
does not implement an event-handler interface.

- **SignalR** — clients connect to the hub at `/signalr-hubs/notifications` (an ABP `AbpHub`, mapped
  **automatically**; the host must *not* call `MapHub`) and receive a trimmed `NotificationDelivery`
  with the recipient list stripped, so siblings' user IDs never leak to each other.
- **Emailing** — resolves each recipient's email address and sends via ABP's `IEmailSender`. Addresses
  come from an ordered `IEmailNotificationAddressResolver` chain: `EmailNotifier` takes the first
  non-null address result. The result may also carry a recipient culture used while building that user's email.
  The base Emailing package registers no resolver, so nothing is sent (and a warning
  is logged) until one exists. Install `Dignite.Abp.Notifications.Emailing.Identity` to get the account
  email as the built-in fallback, and register your own resolver at
  `NotificationEmailProviderOrders.Default` to claim specific notifications — for example, sending an
  *order shipped* mail to the contact address recorded on the order rather than the account address:

  ```csharp
  public class OrderEmailNotificationAddressResolver
      : IEmailNotificationAddressResolver, ITransientDependency
  {
      public int Order => NotificationEmailProviderOrders.Default;

      public async Task<EmailNotificationAddress?> GetEmailOrNullAsync(
          EmailNotificationAddressResolveContext context,
          CancellationToken cancellationToken = default)
      {
          if (context.Notification.NotificationName != "Demo.OrderShipped")
          {
              return null;  // not mine — fall through to the Identity fallback
          }

          var contact = await _orders.FindContactAsync(
              context.Notification.EntityId!, context.UserId, cancellationToken);
          return contact == null
              ? null   // no address — fall through
              : EmailNotificationAddress.To(contact.Email, contact.CultureName);
      }
  }
  ```

  A resolver returns the address **for this user** in this entity context, never "the entity's
  address" — `EmailNotifier` builds the body for that same user and sends one email per recipient.
  The optional `CultureName` on `EmailNotificationAddress` selects the culture for that recipient's content build;
  when it is omitted, `NotificationEmailOptions.DefaultCulture` is used. Culture is scoped only around one
  recipient's content build and is restored before the next recipient is processed. When ABP Setting Management is
  installed, the Identity integration uses its user-targeted setting manager and fallback chain; otherwise the
  configured `NotificationEmailOptions.DefaultCulture` is used.
  Never call `CurrentTenant.Change` in a resolver: ABP's event bus has already entered the
  notification's tenant. `context.TenantId` exists for resolvers that must forward the tenant across a
  boundary the ambient scope cannot cross, such as a remote user service. The host still owns SMTP /
  `IEmailSender` configuration.

  The **body** comes from the parallel `INotificationEmailContentProvider` chain. Derive from
  `NotificationEmailContentProvider<TData>` and the payload is narrowed for you — forgetting the type
  check would otherwise make your provider claim every notification in the system:

  ```csharp
  public class OrderShippedEmailContentProvider
      : NotificationEmailContentProvider<OrderShippedNotificationData>, ITransientDependency
  {
      protected override Task<NotificationEmail?> BuildOrNullAsync(
          NotificationEmailBuildContext context,
          OrderShippedNotificationData data,
          CancellationToken cancellationToken)
      {
          return Task.FromResult<NotificationEmail?>(
              new NotificationEmail($"Order {data.OrderNumber} shipped", RenderBody(data), isBodyHtml: true));
      }
  }
  ```

  Subclasses of `TData` match too, so a provider typed on a base payload keeps handling payloads derived
  from it. Implement `INotificationEmailContentProvider` directly only for the rare provider that handles
  two unrelated payload types.

**Write your own** (Web Push, FCM, SMS, Webhook, …): create a project depending on
`Dignite.Abp.Notifications.Abstractions` **only**, and handle the event:

```csharp
public class WebPushNotifier
    : INotificationNotifier, ITransientDependency
{
    public const string ChannelName = "WebPush";
    public string Name => ChannelName;

    public async Task<NotificationDeliveryResult> DeliverAsync(
        NotificationDeliveryRequestedEto request,
        CancellationToken cancellationToken = default)
    {
        var payload = NotificationDelivery.FromWorkItem(request);
        // Forward request.IdempotencyKey if the provider supports idempotent requests.
        await _webPush.SendAsync(request.UserId, payload, request.IdempotencyKey, cancellationToken);
        return NotificationDeliveryResult.Succeeded();
    }
}
```

For a pre-stable custom notifier, replace
`INotificationNotifier<NotificationDeliveryEto>.HandleEventAsync(NotificationDeliveryEto)` with the contract above.
The old aggregate event exposed many recipients and no result/cancellation boundary; the replacement receives one
recipient/channel request, observes cancellation, forwards its idempotency key, and reports suppression explicitly.
Drain old aggregate events before deploying the upgraded consumer because 10.0 intentionally registers no legacy
aggregate handler.

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
| `POST /api/notifications/subscriptions` | Subscribe to all entities for a notification name (compatibility endpoint) |
| `DELETE /api/notifications/subscriptions/{name}` | Unsubscribe from all entities for a name (compatibility endpoint) |
| `POST /api/notifications/subscription-scopes` | Subscribe to the definition-wide or exact entity scope in the JSON body |
| `DELETE /api/notifications/subscription-scopes` | Unsubscribe only the definition-wide or exact entity scope in the query |
| `GET /api/notifications/deliveries` | Query delivery state by notification, user, channel, state, and time (`NotificationCenter.Deliveries`) |
| `POST /api/notifications/deliveries/{id}/retry` | Retry failed or dead-letter work in the current tenant without overriding preferences (`NotificationCenter.Deliveries.Retry`) |
| `POST /api/notifications/deliveries/{id}/force-deliver` | Explicitly override a suppressed delivery in the current tenant and record the operator audit (`NotificationCenter.Deliveries.ForceDeliver`) |
| `GET /api/notifications/preferences` | List the caller's global, notification, channel, and exact rules |
| `PUT /api/notifications/preferences` | Upsert one caller-owned rule (`notificationName` and `channel` are independently optional) |
| `DELETE /api/notifications/preferences` | Delete exactly the caller-owned rule identified by the nullable query scope |
| `GET /api/notifications/preferences/quiet-hours` | Get the caller's quiet-hours schedule, or `null` |
| `PUT /api/notifications/preferences/quiet-hours` | Set the caller's minute-of-day window and system time-zone ID |
| `DELETE /api/notifications/preferences/quiet-hours` | Disable quiet hours by deleting the caller's schedule |

Inbox and subscription endpoints are scoped to the authenticated caller. Delivery operations are administrative,
permission-gated, and tenant/host scoped; they expose only sanitized diagnostics. Use `...HttpApi.Client` for a
typed C# proxy, or the ABP-generated Angular services for end-user endpoints. Preference routes are exposed through
`NotificationDeliveryPreferencesService`; the Angular library does not add an operator UI for delivery state.

The scoped request contains `notificationName` plus optional `entityTypeName` and `entityId`; the two
entity fields must be supplied together. `GET subscriptions` returns the definition-wide row for each
available definition and every persisted entity-specific row separately, so clients must use the full
three-field scope rather than infer state from a flattened notification name.

For subscription-driven distribution, a notification without an entity matches only definition-wide
subscriptions. A notification for a concrete entity matches the union of definition-wide subscriptions
and that exact entity scope; a user present in both receives one inbox row and one channel delivery.

## UI libraries (optional)

- **MVC** (`Dignite.Abp.NotificationCenter.Web`): a notification-bell view component and a
  subscriptions page. Configure the hub URL and per-type rendering via `NotificationCenterWebOptions`
  — `SignalRHubUrl`, `DataViewComponents` (keyed by discriminator), and `EntityLinkResolvers`.
- **Angular** (`angular/projects/notification-center`): an ABP-generated proxy service plus bell and
  subscriptions components, built against `/api/notifications` and the SignalR hub.

Both UI libraries use the same realtime lifecycle contract: one application-scoped SignalR connection,
deduplicated `ReceiveNotification` handlers, shared refresh events, and a full REST resync after reconnect
because SignalR does not replay missed notifications. Component mount/unmount only retains or releases the
shared runtime. Logout stops the connection; login, token renewal, tenant/account changes, and hub/API URL
changes reconnect with the new context. Angular exposes this as `NotificationRealtimeService` and
`NotificationCenterRealtimeOptions` through `provideNotificationCenterConfig({ realtime: ... })`; MVC exposes
the shared manager as `dignite.notificationCenter.realtime` and reads the hub URL from
`NotificationCenterWebOptions.SignalRHubUrl`. For remote deployments, set the Angular `hubUrl`/`hubPath` or the
MVC `SignalRHubUrl` to the externally reachable hub endpoint while keeping `/api/notifications` as the
authoritative inbox source.

## Configuration

```csharp
Configure<NotificationOptions>(options =>
{
    // Explicit recipients above this count distribute on a background job instead of inline. Default: 5.
    options.DirectDistributionUserThreshold = 10;

    // Each value must be between 1 and NotificationOptions.MaxDistributionBatchSize (10,000).
    options.RecipientBatchSize = 256;
    options.UserNotificationWriteBatchSize = 256;
    // Compatibility name: recipients converted to single-recipient/channel work records per scheduling group.
    options.DeliveryEventRecipientLimit = 100;

    // Per-recipient/channel claim, retry, and dead-letter policy.
    options.DeliveryLeaseDuration = TimeSpan.FromMinutes(2);
    options.MaxDeliveryAttempts = 5;
    options.InitialDeliveryRetryDelay = TimeSpan.FromSeconds(10);
    options.MaxDeliveryRetryDelay = TimeSpan.FromMinutes(15);
    options.DeliveryRetryBackoffFactor = 2;
    options.DeliveryRetryJitterFactor = 0.2;
    options.DeliveryRetryWorkerPeriod = TimeSpan.FromSeconds(30);
    options.DeliveryRetryBatchSize = 100;
    options.IsDeliveryRetryWorkerEnabled = true;
});

Configure<NotificationEmailOptions>(options =>
{
    // Used when an email address resolver does not supply a recipient culture.
    options.DefaultCulture = "en-US";
});

// EF Core Notification Center hosts can opt in to ABP's transactional outbox so the persisted
// inbox rows and NotificationDeliveryRequestedEto outbox records commit together. The channel consumer
// persists its delivery ledger before claiming the work.
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.UseNotificationCenterEfCoreOutbox();
});

// MongoDB hosts use the equivalent opt-in. Host startup performs a real transaction probe;
// use a transaction-capable MongoDB 4.0+ replica set and transactional ABP units of work.
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.UseNotificationCenterMongoDbOutbox();
});
```

## Architecture

```
Notifications.Abstractions   ── data model + NotificationDeliveryRequestedEto + reliable notifier contract
        │
Notifications (Core)         ── define → distribute → publish work; channel consumers claim/retry
        │
   ┌────┴───────────────────┐
Notifiers                 NotificationCenter (optional)
(SignalR / Email / …)     durable delivery ledger · inbox · subscriptions · REST API · UI
                                                       (EF Core / MongoDB)
```

**Publish → distribute → notify:**

1. Business code calls `INotificationPublisher.PublishAsync(...)`.
2. Small explicit fan-outs distribute inline; larger ones enqueue a `NotificationDistributionJob`.
3. The distributor resolves bounded recipient pages (explicit `userIds`, or subscribers from
   `INotificationStore`), checks the definition's feature/permission availability, persists bounded
   inbox groups (a no-op under `NullNotificationStore`), creates one delivery identity per recipient/channel,
   then publishes `NotificationDeliveryRequestedEto` when external channels are configured.
4. Only a process hosting the selected channel handles the work. In one independently committed store operation it
   inserts a new self-contained delivery snapshot directly as a claimed lease, or atomically claims an existing due
   row; it then invokes the notifier and records success, suppression, retry timing, or dead-letter state. Its retry
   worker republishes due or lease-expired work.

`NotificationDeliveryRequestedEto` is the load-bearing boundary between scheduling and delivery and the extension
point for any new channel. Under either Notification Center persistence provider, hosts should opt in to ABP's
transactional outbox (see [Configuration](#configuration)) so notification, inbox, and outgoing work records
commit together. The delivery ledger belongs to the consuming channel application; initial materialization and
claim commit together independently of the consumer's ambient event-inbox transaction.

> **Serialization invariant:** every `NotificationData` subclass must carry a stable
> `[NotificationDataType]` discriminator plus an explicit schema version and round-trip through
> System.Text.Json only — never a CLR type name / `AssemblyQualifiedName`, never Newtonsoft. Versionless
> historical JSON is v1; breaking shapes advance the attribute version and provide every deterministic
> N→N+1 upcaster. That is what keeps historical and remote payloads readable and lets non-.NET clients render them.

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

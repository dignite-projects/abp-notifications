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
enables ABP's transactional outbox. The process that actually hosts the selected channel consumes the work event
and calls the channel notifier once; processes that do not host that channel ignore the event. Delivery is
best-effort — there is no per-recipient delivery record, lease, or retry (see invariant §4) — so the inbox row is
the authoritative record. Atomic rollback of the inbox writes requires an ambient **transactional** ABP unit of
work; an outbox cannot make a non-transactional unit of work atomic.

| Setup | Persist + publish atomic | Failure/cancellation after a completed batch |
|---|---|---|
| EF Core, outbox + transactional UoW | yes, within one distributor/job invocation | the transaction rolls back inbox and outbox records |
| EF Core, no outbox + transactional UoW | no | inbox writes roll back, but an already published external event may have escaped |
| EF Core, non-transactional UoW | no | completed inbox batches and already published events can remain |
| MongoDB, outbox + transactional UoW on a supported topology | yes, within one distributor/job invocation | the transaction rolls back inbox and outbox records |
| MongoDB, no outbox or non-transactional UoW | no | completed inbox batches and already published events can remain |
| Core-only channel consumer | no inbox | the channel event is fire-once; nothing is retained across process exit |

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

The transactional outbox requires a transaction-capable MongoDB deployment: a replica set running MongoDB 4.0 or
later with logical sessions. Standalone servers cannot commit the outbox transaction. The host must also use
transactional ABP units of work; do not set `AbpUnitOfWorkDefaultOptions.TransactionBehavior` to `Disabled`. For
production with more than one application instance, configure an ABP distributed-lock provider for the outbox
sender and inbox processor. (An earlier release shipped a startup self-probe that opened a throwaway transaction to
verify this; it was removed as over-engineering — it duplicated ABP/driver transaction-capability detection. Rely
on your deployment topology instead.)

#### MongoDB upgrade, collections, indexes, and cleanup

Existing MongoDB hosts should upgrade in this order:

1. convert the deployment to a MongoDB 4.0+ replica set and verify transactions independently;
2. ensure the application role can create indexes and read/write the ABP event-box collections;
3. inspect existing `AbpEventInbox` records and collapse duplicate non-empty `MessageId` values before the new
   unique index is created (retain the processed/discarded record when one exists, otherwise the oldest pending row);
4. deploy the new provider package and add `UseNotificationCenterMongoDbOutbox()`;
5. keep notification distribution inside transactional units of work, then monitor the ABP outbox/inbox workers
   before enabling traffic.

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

Ensure every resolved database is a transaction-capable replica set before accepting traffic.

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

#### Retention and lifecycle cleanup

Notification Center retention is opt-in and best-effort. The ABP periodic cleanup worker is disabled by default, so
upgrading preserves the historical behavior of retaining inbox and base payload rows until an application or user
explicitly deletes them. Enable it only after setting retention windows that match your operational and legal
requirements:

```csharp
Configure<NotificationRetentionOptions>(options =>
{
    options.IsCleanupEnabled = true;
    options.CleanupBatchSize = 500;              // max rows deleted per record kind and pass
    options.CleanupWorkerPeriod = TimeSpan.FromHours(6);
    options.CleanupWorkerLockName = NotificationRetentionOptions.DefaultCleanupWorkerLockName;
    options.CleanupWorkerLockTimeout = TimeSpan.Zero; // skip immediately when another instance owns the cycle
    options.ReadUserNotificationRetention = TimeSpan.FromDays(180); // null disables read-inbox cleanup
    options.OrphanNotificationRetention = TimeSpan.FromDays(30);    // null disables orphan-payload cleanup
});
```

`NotificationRetentionManager.CleanupAsync(cancellationToken)` deletes, in bounded batches: `Read` inbox rows older
than `ReadUserNotificationRetention` (`Unread` rows are always retained), and `Notification` payload rows older than
`OrphanNotificationRetention` once no inbox row references them. A host can invoke it directly or enable the worker.
There is no deletion cursor, dry-run mode, deletion contributor, quarantine marker, or retention metric — those were
removed as over-engineering for an in-app inbox.

| Record | Owner | Deletion rule |
|---|---|---|
| `UserNotification` inbox row | Notification Center / current user | Users delete their own rows. Cleanup deletes `Read` rows older than `ReadUserNotificationRetention`; `Unread` rows are always retained. |
| `Notification` base payload | Notification Center retention cleanup | Deleted when older than `OrphanNotificationRetention` and no inbox row still references it. |
| `NotificationSubscription` | User subscription settings | Not time-based. Delete only by the exact subscription identity through the subscription APIs. |
| ABP event inbox/outbox records | ABP distributed event bus | Use ABP's status-aware event-box cleanup windows. Do not add TTL deletes that bypass processed/in-progress state. |

#### The retention worker in clustered deployments

The retention cleanup scanner is registered through ABP's background-worker manager as an
`AsyncPeriodicBackgroundWorkerBase`. Every cycle gets a fresh dependency-injection scope, observes the application
shutdown token, and tries its stable lock without waiting by default; a lock miss skips the cycle.

`Volo.Abp.DistributedLocking.Abstractions` supplies only a process-local default lock. Multiple application
instances therefore require a real ABP distributed-lock provider (for example, the provider already used by the
host's background jobs). All instances that scan the same notification store must use the same
`CleanupWorkerLockName` and the same `AbpDistributedLockOptions.KeyPrefix`. For a dedicated-worker deployment,
keep ABP workers enabled only in the worker process (`AbpBackgroundWorkerOptions.IsEnabled = false` on web/API
replicas), or leave them enabled and set `IsCleanupEnabled = false` on replicas that should not run the scanner.

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

### Reading persisted payloads (tolerant reads)

The wire/storage envelope carries only the stable `type` discriminator — there is no schema-version field or
upcaster chain (an earlier design added event-sourcing-style versioning + N→N+1 upcasters; it was removed as
over-engineering, since notifications are read-once, not a replayable event stream). To change a payload's JSON
shape, prefer adding members: a newer payload of a *known* type reads leniently, with unknown members landing in
`ExtensionData`.

`INotificationDataSerializer.Deserialize(json, readMode)` is the single programmatic read boundary. Callers select
`NotificationDataReadMode.Strict` for trusted/corruption-sensitive reads or `Tolerant` for durable and batch reads.
Strict mode throws a typed `NotificationDataReadException`. Notification Center inbox reads, distributed-event
deserialization, and HTTP server/client converters use tolerant mode: an unknown discriminator or malformed known
payload becomes `UnsupportedNotificationData`, preserving the original discriminator and escaped raw JSON without
activating an arbitrary CLR type. One bad historical row therefore cannot fail the rest of an inbox page. The MVC
and Angular libraries render this placeholder as a generic unsupported-notification message and do not display its
raw diagnostic JSON.

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

Definition names use ordinal, case-sensitive comparison. Every duplicate name is a startup error, and the error
identifies both provider types; an equivalent-looking second definition is not treated as idempotent because
definitions are mutable after construction. Provider types are convention-discovered across modules; registering
the same provider type more than once is idempotent and the provider executes once. Empty and whitespace-only
definition names are rejected by the constructor.

An explicitly empty `userIds` array remains a true no-op and returns before definition resolution.

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

### Recipient eligibility

A definition's permission and feature requirements govern both who may subscribe **and who may receive** the
notification. The distributor applies the same `INotificationDefinitionManager.IsAvailableAsync` filter to both
subscription-derived and explicitly targeted candidates: caller-supplied exclusions are removed first, then each
remaining candidate must satisfy `PermissionName` and `FeatureName` (configured through `RequirePermission(...)` /
`RequireFeature(...)`) or is filtered out without an inbox row or channel event. An explicit `userIds` array is
**not** an authorization bypass. This is a delivery policy, not publisher authorization: publishing code still
needs its own application permission checks where appropriate.

Eligibility is evaluated in the notification's recorded `TenantId`, not whichever tenant happens to be ambient
when an inline call or background job executes. A tenant notification therefore uses that tenant's feature values
and permission context, while a host notification is evaluated in the host context. Recipient IDs are never logged.

### Bounded recipient pipeline

Both explicit and subscription-derived recipients flow through the same bounded pipeline: resolve a candidate
batch → filter by definition eligibility → write inbox rows → publish one `NotificationDeliveryRequestedEto` per
eligible recipient/channel. The batch size is `NotificationDistributionOptions.RecipientBatchSize` (default 256;
must be between 1 and `MaxBatchSize` = 10,000, validated at host startup). The built-in subscription scan uses a
database-side distinct query with an exclusive user-ID keyset cursor rather than offset paging, so inserts/deletes
before the cursor cannot repeat or skip later recipients. `NullNotificationStore` implements the same contract
without persistence. All store operations accept a cancellation token, observed between candidate, persistence,
and delivery batches (not during a provider operation already in flight). Notification data must fit the chosen
transport's message-size limit.

`PublishAsync` distributes explicit fan-outs at or below `DirectDistributionUserThreshold` (default 5) inline, and
larger ones through a single background job carrying the caller's `Guid[]`; the job's distributor batches
recipients internally. `DirectDistributionUserThreshold` is capped by the same 10,000 safeguard.

`INotificationPublisher` records `CurrentTenant.Id` automatically. Code that calls `INotificationDistributor`
directly must populate `NotificationInfo.TenantId` for tenant notifications. That value is authoritative for
subscription lookup, eligibility, inbox persistence, and event/outbox publication; `null` explicitly means
**host**, even when the direct caller currently has an ambient tenant.

## Notifiers

A notifier implements the single canonical `INotificationNotifier` contract and relays one
`NotificationDeliveryRequestedEto` to a single channel. `Name` is the stable routing key. `DeliverAsync` receives a
`CancellationToken`; delivery is best-effort, so a notifier that intentionally skips a recipient simply returns,
and throwing is logged and dropped by the Core handler (not retried). The Core-owned distributed-event handler is
the transport adapter, so a channel plugin does not implement an event-handler interface.

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

    public async Task DeliverAsync(
        NotificationDeliveryRequestedEto request,
        CancellationToken cancellationToken = default)
    {
        var payload = NotificationDelivery.FromWorkItem(request);
        await _webPush.SendAsync(request.UserId, payload, cancellationToken);
    }
}
```

`DeliverAsync` handles one recipient/channel request and observes cancellation. Delivery is best-effort: to skip a
recipient (e.g. no address), simply return; throwing is logged and dropped by the Core handler, not retried.

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

All endpoints are scoped to the authenticated caller. Use `...HttpApi.Client` for a typed C# proxy, or the
ABP-generated Angular services.

The scoped request contains `notificationName` plus optional `entityTypeName` and `entityId`; the two
entity fields must be supplied together. `GET subscriptions` returns the definition-wide row for each
available definition and every persisted entity-specific row separately, so clients must use the full
three-field scope rather than infer state from a flattened notification name.

For subscription-driven distribution, a notification without an entity matches only definition-wide
subscriptions. A notification for a concrete entity matches the union of definition-wide subscriptions
and that exact entity scope; a user present in both receives one inbox row and one channel delivery.

### Pre-stable application/domain API migration

The REST routes above are unchanged. Recompile consuming code and regenerate ABP clients after applying these
source-level changes:

| Before | Now | Consumer action |
|---|---|---|
| `INotificationAppService` / `NotificationAppService` | `IUserNotificationAppService` / `UserNotificationAppService` | Rename injected service types and replacements. |
| `GetCountAsync` | `GetNotificationCountAsync` | Rename C# calls; the route remains `GET /api/notifications/count`. |
| `IUserNotificationManager` / `UserNotificationManager` | removed | Use `INotificationStore` for inbox queries and state mutations. |
| `INotificationSubscriptionManager` | concrete `NotificationSubscriptionManager` | Use the manager only for validated subscription mutations; use `INotificationStore` for reads. |
| Subscription-manager read methods | removed | Use `INotificationStore.GetSubscriptionsAsync` / `IsSubscribedAsync` directly in query paths. |
| `INotificationRetentionCleanupService` / `NotificationRetentionCleanupService` | `NotificationRetentionManager` | Inject the concrete domain manager for manual cleanup. |

`INotificationDefinitionManager` remains intentionally replaceable because consuming hosts can provide a custom
definition registry and availability policy; startup resolves that replacement before definition initialization.
`INotificationStore` likewise remains a genuine extension boundary.

## UI libraries (optional)

- **MVC** (`Dignite.Abp.NotificationCenter.Web`): a notification-bell view component and a
  subscriptions page. Configure the hub URL and per-type rendering via `NotificationCenterWebOptions`
  — `SignalRHubUrl`, `DataViewComponents` (keyed by discriminator), and `EntityLinkResolvers`.
- **Angular** (`angular/projects/notification-center`): an ABP-generated proxy service plus bell and
  subscriptions components, built against `/api/notifications` and the SignalR hub.

Both bells open a SignalR connection to `/signalr-hubs/notifications` and refresh from the REST inbox when a
`ReceiveNotification` message arrives or the connection reconnects (auto-reconnect handled by the SignalR client);
the REST inbox is always the authoritative source, since SignalR does not replay missed notifications. If the
SignalR notifier isn't installed or `@microsoft/signalr` isn't loaded, the bell degrades to a non-live view. MVC
reads the hub URL from `NotificationCenterWebOptions.SignalRHubUrl`; for remote deployments point that (or the
Angular API URL) at the externally reachable hub while keeping `/api/notifications` as the inbox source.

## Configuration

```csharp
Configure<NotificationDefinitionOptions>(options =>
{
    // Provider types are normally convention-discovered; explicit registration is also supported.
    options.DefinitionProviders.Add(typeof(MyNotificationDefinitionProvider));
});

Configure<NotificationDistributionOptions>(options =>
{
    // Explicit recipients above this count distribute on a background job instead of inline. Default: 5.
    options.DirectDistributionUserThreshold = 10;

    // Recipients resolved, persisted, and published per batch. Between 1 and MaxBatchSize (10,000). Default: 256.
    options.RecipientBatchSize = 256;
});

Configure<NotificationEmailOptions>(options =>
{
    // Used when an email address resolver does not supply a recipient culture.
    options.DefaultCulture = "en-US";
});

// Retention cleanup is opt-in — see "Retention and lifecycle cleanup" above.
Configure<NotificationRetentionOptions>(options => options.IsCleanupEnabled = true);

// EF Core Notification Center hosts can opt in to ABP's transactional outbox so the persisted
// inbox rows and NotificationDeliveryRequestedEto outbox records commit together.
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.UseNotificationCenterEfCoreOutbox();
});

// MongoDB hosts use the equivalent opt-in on a transaction-capable MongoDB 4.0+ replica set with
// transactional ABP units of work.
Configure<AbpDistributedEventBusOptions>(options =>
{
    options.UseNotificationCenterMongoDbOutbox();
});
```

## Architecture

```
Notifications.Abstractions   ── data model + NotificationDeliveryRequestedEto + notifier contract
        │
Notifications (Core)         ── define → distribute → publish delivery events (best-effort)
        │
   ┌────┴───────────────────┐
Notifiers                 NotificationCenter (optional)
(SignalR / Email / …)     inbox · subscriptions · REST API · UI
                                                       (EF Core / MongoDB)
```

**Publish → distribute → notify:**

1. Business code calls `INotificationPublisher.PublishAsync(...)`.
2. Small explicit fan-outs distribute inline; larger ones enqueue a `NotificationDistributionJob`.
3. The distributor resolves bounded recipient batches (explicit `userIds`, or subscribers from
   `INotificationStore`), checks the definition's feature/permission availability, persists bounded
   inbox groups (a no-op under `NullNotificationStore`), then publishes one `NotificationDeliveryRequestedEto`
   per recipient/channel when external channels are configured.
4. Only a process hosting the selected channel handles the work: the Core-owned event handler resolves the channel
   notifier and calls `DeliverAsync` once. Delivery is best-effort — a channel that throws is logged and dropped,
   not retried; the inbox row is the authoritative record.

`NotificationDeliveryRequestedEto` is the load-bearing boundary between scheduling and delivery and the extension
point for any new channel. Under either Notification Center persistence provider, hosts should opt in to ABP's
transactional outbox (see [Configuration](#configuration)) so notification, inbox, and outgoing work records
commit together.

> **Serialization invariant:** every `NotificationData` subclass must carry a stable
> `[NotificationDataType]` discriminator and round-trip through System.Text.Json only — never a CLR type name /
> `AssemblyQualifiedName`, never Newtonsoft. The envelope carries only that discriminator (no schema version, no
> upcaster chain); a newer payload of a known type reads leniently. That is what keeps historical and remote
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

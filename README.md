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
`Dignite.Abp.NotificationCenter.MongoDB`. Permission gating through
`Dignite.Abp.Notifications.Identity` is also optional.

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

    public MyHostDbContext(DbContextOptions<MyHostDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ConfigureNotificationCenter();   // maps the three tables
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

#### Delivery guarantees — opt in to the transactional outbox

Distributing a notification writes the per-user inbox rows **and** publishes `NotificationDeliveryEto`
for the notifiers. Those two commit together only when the host enables ABP's transactional outbox;
the same call enables the inbox, which deduplicates a redelivered event so a notifier does not send
twice.

| Setup | Persist + publish atomic | Redelivery deduplicated |
|---|---|---|
| EF Core, outbox enabled | yes | yes |
| EF Core, outbox not enabled | no | no |
| MongoDB | no | no |

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
    // ...the three notification DbSets

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

The MongoDB provider wires no outbox or inbox. A crash between the store write and the publish leaves
the notification in the inbox with no channel delivery, and a redelivered event makes the Email
notifier send a second copy. Choose EF Core if those guarantees matter.

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
counts are logged at Information level and the corresponding user IDs at Debug level. Replace
`INotificationRecipientEligibilityEvaluator` when a deployment can batch these lookups more efficiently;
the replacement must preserve the notification tenant/host boundary and return the same eligible/excluded
partition.

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

A notifier is an `INotificationNotifier<NotificationDeliveryEto>` that relays the event to a single channel.
The generic notifier contract includes ABP's `IDistributedEventHandler<TEvent>` contract, while the
non-generic `INotificationNotifier` keeps the stable channel metadata (`Name`) available for channel
enumeration and routing.

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

      public async Task<EmailNotificationAddress?> GetEmailOrNullAsync(EmailNotificationAddressResolveContext context)
      {
          if (context.Notification.NotificationName != "Demo.OrderShipped")
          {
              return null;  // not mine — fall through to the Identity fallback
          }

          var contact = await _orders.FindContactAsync(context.Notification.EntityId!, context.UserId);
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
          NotificationEmailBuildContext context, OrderShippedNotificationData data)
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
| `POST /api/notifications/subscriptions` | Subscribe to all entities for a notification name (compatibility endpoint) |
| `DELETE /api/notifications/subscriptions/{name}` | Unsubscribe from all entities for a name (compatibility endpoint) |
| `POST /api/notifications/subscription-scopes` | Subscribe to the definition-wide or exact entity scope in the JSON body |
| `DELETE /api/notifications/subscription-scopes` | Unsubscribe only the definition-wide or exact entity scope in the query |

Every endpoint is scoped to the authenticated caller. Use `...HttpApi.Client` for a typed C# proxy,
or the Angular `NotificationsService` proxy in the browser.

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

## Configuration

```csharp
Configure<NotificationOptions>(options =>
{
    // Explicit recipients above this count distribute on a background job instead of inline. Default: 5.
    options.DirectDistributionUserThreshold = 10;
});

Configure<NotificationEmailOptions>(options =>
{
    // Used when an email address resolver does not supply a recipient culture.
    options.DefaultCulture = "en-US";
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

# Hard Invariants — Read Before Touching Notifications / Notifiers / DI Lifetimes

> This file has **no `paths:` frontmatter, so it always loads**, alongside `abp-core.md` and
> `template/app.md`. It exists because this repo is a from-scratch rewrite of a legacy
> implementation that shipped with the exact bugs described below —
> these are not style preferences, they're the reasons this repo exists. Do not reintroduce them.

## 1. `NotificationData` serialization: stable discriminator, System.Text.Json only

**Never** use `data.GetType().AssemblyQualifiedName`/`.FullName` or `Type.GetType(...)` as the
wire/storage type identifier for a `NotificationData` subclass, and **never** mix in
Newtonsoft.Json anywhere in this pipeline.

- Every `NotificationData` subclass must carry a short, stable discriminator via
  `[NotificationDataType("...")]` (e.g. `"Dignite.Message"`) — a name that survives assembly
  version bumps and works for remote/non-.NET consumers reading the same JSON.
- Discriminators use ordinal, case-sensitive comparison and must form a one-to-one mapping with CLR
  payload types. Registering one key for two types, or one type under two keys, fails during startup;
  repeating the exact same key/type pair is the only supported idempotent repeat.
- All serialization — storage (EF Core / MongoDB), the distributed event bus, and the HTTP API —
  must go through the shared `INotificationDataTypeRegistry` / `NotificationDataJsonConverter`
  (registered once, globally, on `AbpSystemTextJsonSerializerOptions` in
  `AbpNotificationsAbstractionsModule.ConfigureServices`, so independently hosted Notifiers receive it too),
  not a one-off converter or a hand-rolled switch
  statement in a specific layer.
- The payload envelope always writes an explicit positive integer `schemaVersion`; versionless historical
  JSON is schema v1. A breaking payload-shape change keeps the discriminator, advances the attribute's current
  version, and registers every deterministic N→N+1 JSON upcaster. Duplicate, missing, or out-of-range steps fail
  at startup; never make an upcaster depend on ambient tenant/user/time or mutate the reserved envelope members.
- Trusted reads stay strict and expose a typed failure reason. Durable inbox, distributed-event, and HTTP reads
  are tolerant: unknown discriminators, future versions, malformed known data, and failed upcasts become
  `UnsupportedNotificationData`. Preserve raw JSON only as escaped diagnostic data; never interpret it as a CLR
  name or show it in the fallback UI.
- **Why**: the legacy implementation wrote with System.Text.Json + `AssemblyQualifiedName`, read
  back with Newtonsoft + `Type.GetType()`, and had a separate hardcoded switch (only 2 types) in
  the HTTP client converter. Result: an assembly version bump could make historical notifications
  undeserializable, and custom `NotificationData` subclasses silently broke for any remote .NET
  client. This was priority **P0** — the "load-bearing wall" bug, because every Notifier and every
  remote consumer sits downstream of it.
- Add a round-trip test for any new `NotificationData` subclass: publish → persist → deserialize on
  a "remote" client, and assert the JSON contains your discriminator + current schema version, not a CLR type name.
  A version bump also needs historical JSON fixtures and old-producer/new-consumer plus
  new-producer/older-schema-aware-consumer event tests.

**The same rule governs `EntityTypeName`.** `NotificationEntityIdentifier` takes a stable,
caller-chosen string — `new NotificationEntityIdentifier("Demo.Order", orderId)`, never
`typeof(Order)`. That value is persisted on `Notification` and `NotificationSubscription`, matched by
plain string equality when `NotificationStore` resolves subscribers, returned over REST as
`UserNotificationDto.EntityTypeName`, and used as the key of
`NotificationCenterWebOptions.EntityLinkResolvers`. A `Type.FullName` there silently orphans every
stored subscription and breaks every bell link the day someone renames a namespace — the same failure
mode as above, on a different field.

## 2. DI lifetime discipline — never let a Singleton capture a Scoped dependency

Before marking a service `ISingletonDependency`, check every constructor dependency (transitively)
for anything backed by a repository, `DbContext`, or other per-request/per-unit-of-work state
(`INotificationStore` in Center mode is exactly this). If it's request-scoped, the service must be
`ITransientDependency` (or resolve the scoped dependency from `IServiceProvider` on demand, not via
constructor injection).

- **Why**: the legacy `UserNotificationManager` was `ISingletonDependency` while injecting
  `INotificationStore` (which, with Center installed, holds repositories/DbContext) — a
  copy-paste/typo bug from its sibling `NotificationSubscriptionManager` (correctly transient).
  Autofac doesn't validate this by default, so it doesn't fail at startup; it fails under
  concurrent load with thread-unsafe `DbContext` use or a UoW that doesn't match the current
  request. Definition/registry caches (name → definition lookups) are fine as singletons — the
  permission/store checks that ride along with them are not.

## 3. Notifiers are plugins — depend on `Abstractions`, not Core or Center

A Notifier (SignalR, Email, future WebPush/FCM/SMS/Webhook) references
`Dignite.Abp.Notifications.Abstractions` and its own channel SDK — nothing else in this repo. It
reacts to `NotificationDeliveryEto` (an `IDistributedEventHandler<NotificationDeliveryEto>`); it
should not need `INotificationStore`, EF Core, or MongoDB. This is what lets a channel be added,
removed, or deployed independently without touching Core.

There is **no exception left** — `Emailing` used to be one and no longer is; its `.csproj` has a
single `ProjectReference`, to `Abstractions`. Don't reintroduce one. Host-specific address or
identity lookups belong in a separate integration package, as `Notifications.Emailing.Identity` does
for ABP Identity. Everything a Notifier needs about the notification — including the entity it
concerns (`EntityTypeName` / `EntityId`) — rides on the ETO.

## 4. Don't leak other recipients through the distributed event

`NotificationDeliveryEto` currently carries the full `Guid[] UserIds` of every recipient of a
notification. A Notifier that relays the ETO payload as-is to each user's client will leak sibling
recipients' user IDs to each other. When writing or touching a Notifier, deliver a per-recipient
view (or at least strip `UserIds` down before it reaches the client), don't forward the raw ETO.

## 5. Both operation modes must keep working

Core logic (publish, distribute, definitions) must function with `NullNotificationStore` (no
Center installed) as well as a real store. Don't add a feature to Core that silently assumes
`NotificationCenter` is present — see "Two operation modes" in `template/app.md`.

## 6. Subscription scope is part of the identity

A subscription is identified by tenant, user, notification definition name, and its optional entity
scope. `EntityTypeName` and `EntityId` are an all-or-nothing pair: both null means every entity for the
definition; both present means one exact entity. Never flatten stored subscriptions to the definition
name in application contracts or UI state.

- Exact checks and deletes operate on the complete identity. Removing one entity scope must not remove
  the definition-wide row or a different entity scope.
- A notification without an entity matches only definition-wide rows. A notification with an entity
  matches definition-wide rows plus its exact entity row, and the distributor deduplicates users in
  that union.
- `SubscribeToAllAvailableNotificationsAsync` creates definition-wide rows only. An existing exact
  entity row does not make the definition-wide row redundant.
- Persistence uniqueness includes the tenant and complete scope. Nullable database uniqueness differs
  across providers, so use the normalized non-null identity keys mapped by both EF Core and MongoDB.

## 7. Definition requirements apply again at delivery

`NotificationDefinition.PermissionName` and `FeatureDependency` constrain both subscription and delivery.
Never treat an explicit `userIds` array as an implicit authorization or eligibility bypass. Explicit and
subscription-derived candidates flow through the same `INotificationRecipientEligibilityEvaluator`, after
caller-supplied exclusions and before inbox persistence or channel publication.

- Evaluate in the notification's recorded `TenantId`; do not inherit an unrelated ambient tenant from an
  inline caller, background worker, or retry. Host (`null`) and tenant contexts must never mix. Direct
  `INotificationDistributor` callers must populate tenant notifications explicitly; `null` is authoritative host,
  not an instruction to fall back to ambient state.
- Keep the contract batch-shaped even when the default implementation checks users individually, so a host
  can replace it with an efficient remote or bulk policy evaluator without forking distribution.
- The only supported bypass is the narrowly named explicit-recipient trusted-system API. It must never resolve
  subscriptions, must bypass both permission and feature requirements together, and must remain observable.
- A failed requirement filters that candidate before any `UserNotificationInfo` or `NotificationDeliveryEto`
  is produced. Publishing authorization is a separate application-layer concern.

## 8. Definition payload/entity contracts validate before side effects

A definition may bind itself to a stable payload discriminator and to a forbidden, optional, or required entity
identity. Treat these as publish-time contracts, not hints for a downstream Notifier or UI.

- `WithPayload<TData>()` derives its contract from `[NotificationDataType]`; startup must verify that exact
  discriminator is registered to the intended CLR type. Runtime matching uses the registry and ordinal stable
  discriminator, never `Type.FullName`, `AssemblyQualifiedName`, or Newtonsoft metadata.
- An opted-in payload contract requires data. Reject missing, unregistered, or differently discriminated data
  before background enqueue, store writes, or distributed event publication.
- Entity requirements use the complete `NotificationEntityIdentifier`. If a definition constrains an entity type,
  compare its stable caller-chosen name ordinally. Never translate it back to a CLR `Type`.
- The trusted-recipient eligibility bypass does not bypass payload/entity validation; the policies answer different
  questions.
- `Unspecified` is the compatibility state. A definition that has not opted into a dimension remains permissive for
  that dimension so migration can happen definition by definition.

## 9. Recipient work stays bounded

Inline/background selection is not a scalability boundary. Explicit and subscription-derived recipients must join
the same bounded candidate → eligibility → persistence → delivery pipeline after resolution.

- Built-in stores page a stable, database-side distinct user-ID query with an exclusive keyset cursor and
  multi-insert only an already-bounded inbox group. Never use offset paging for a mutable subscription set.
  Matching definition-wide and exact entity subscriptions must not create duplicate inbox rows.
- A background job must not carry the whole explicit fan-out. Prepare shared notification state once, then schedule
  bounded explicit batches; preserve tenant and eligibility mode on every job.
- EF Core must flush and detach each completed inbox group so the change tracker does not become a hidden
  notification-wide collection. Atomic rollback requires an ambient transactional UoW.
- Never put every recipient into one `NotificationDeliveryEto`; respect
  `NotificationOptions.DeliveryEventRecipientLimit`. Broker limits also include the notification payload.
- Observe cancellation between candidate, store, and event batches. Do not describe cancellation as rollback:
  EF/outbox, EF without outbox, MongoDB, and null-store forwarding have different partial-progress semantics.
- Keep candidates, eligible recipients, filtered recipients, batches, duration, and failures observable without
  logging recipient IDs. Preserve tenant scope on every query, write, metric tag, job, and event.
- `IBatchedNotificationStore`, `ICancellableNotificationDistributor`, and `IPreparedNotificationDistributor` are
  additive compatibility capabilities. Legacy custom implementations may use the compatibility fallback, but large
  fan-outs require those capabilities.

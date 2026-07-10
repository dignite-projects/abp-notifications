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
- All serialization — storage (EF Core / MongoDB), the distributed event bus, and the HTTP API —
  must go through the shared `INotificationDataTypeRegistry` / `NotificationDataJsonConverter`
  (registered once, globally, on `AbpSystemTextJsonSerializerOptions` in
  `AbpNotificationsModule.ConfigureServices`), not a one-off converter or a hand-rolled switch
  statement in a specific layer.
- **Why**: the legacy implementation wrote with System.Text.Json + `AssemblyQualifiedName`, read
  back with Newtonsoft + `Type.GetType()`, and had a separate hardcoded switch (only 2 types) in
  the HTTP client converter. Result: an assembly version bump could make historical notifications
  undeserializable, and custom `NotificationData` subclasses silently broke for any remote .NET
  client. This was priority **P0** — the "load-bearing wall" bug, because every Notifier and every
  remote consumer sits downstream of it.
- Add a round-trip test for any new `NotificationData` subclass: publish → persist → deserialize on
  a "remote" client, and assert the JSON contains your discriminator string, not a CLR type name.

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

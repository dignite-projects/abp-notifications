# Dignite.Abp.Notifications — Solution Structure

> **Docs**: https://abp.io/docs/latest/solution-templates/module-development-template

This repo is **class libraries only** — two sibling, independently distributable ABP module
trees. There is no `Host`, no `DbMigrator`, no frontend. A separate host application (not in this
repo) references these projects (or their NuGet packages) and owns the actual running app.

## Solution structure

```
abp-notifications/
├── Dignite.Abp.Notifications.slnx           # core framework solution
├── Dignite.Abp.NotificationCenter.slnx       # optional persistence + REST API solution
├── Directory.Build.props                     # shared MSBuild props (LangVersion, Nullable, Version, license)
├── Directory.Packages.props                  # ⚠️ central package management — ALL package versions live here
├── core/
│   ├── src/
│   │   ├── Dignite.Abp.Notifications.Abstractions/  # contracts shared by everything: NotificationData,
│   │   │                                             # RealTimeNotifyEto, INotificationNotifier, registries.
│   │   │                                             # Multi-targeted (netstandard2.0/2.1/net10.0) — Notifiers
│   │   │                                             # and remote consumers depend on ONLY this.
│   │   ├── Dignite.Abp.Notifications/               # the core: definitions, publishing, distribution,
│   │   │                                             # INotificationStore abstraction + NullNotificationStore,
│   │   │                                             # subscription/user-notification managers. net10.0.
│   │   ├── Dignite.Abp.Notifications.Identity/      # pluggable INotificationPermissionChecker via ABP Identity
│   │   ├── Dignite.Abp.Notifications.Emailing/      # Email Notifier plugin
│   │   └── Dignite.Abp.Notifications.SignalR/       # SignalR Notifier plugin (real-time push)
│   └── test/Dignite.Abp.Notifications.Tests/
└── notification-center/
    ├── src/
    │   ├── Dignite.Abp.NotificationCenter.Domain.Shared/     # constants, enums (NotificationSeverity, UserNotificationState)
    │   ├── Dignite.Abp.NotificationCenter.Domain/            # Notification / UserNotification / NotificationSubscription aggregates
    │   ├── Dignite.Abp.NotificationCenter.Application.Contracts/  # DTOs, service interfaces, permissions
    │   ├── Dignite.Abp.NotificationCenter.Application/       # AppService implementations
    │   ├── Dignite.Abp.NotificationCenter.HttpApi/           # Auto API Controllers
    │   ├── Dignite.Abp.NotificationCenter.HttpApi.Client/    # C# client proxies for remote consumers
    │   ├── Dignite.Abp.NotificationCenter.EntityFrameworkCore/  # INotificationStore impl #1 (relational)
    │   └── Dignite.Abp.NotificationCenter.MongoDB/           # INotificationStore impl #2 (document)
    └── test/Dignite.Abp.NotificationCenter.Tests/
```

## File layout convention

Source files live under `<ProjectFolder>/<mirrored namespace path>/File.cs`, **not** a generic
`Entities/`/`Services/` split — e.g. the `Dignite.Abp.Notifications` namespace lives at
`core/src/Dignite.Abp.Notifications/Dignite/Abp/Notifications/*.cs`. Every `.csproj` sets
`<RootNamespace />` (empty) specifically so the C# SDK derives each file's namespace from its
folder path. When adding a new file, put it at the folder path matching its namespace — don't add
a flat `Entities/` or `Services/` subfolder.

(Exception: the two `test` projects that put `.cs` files directly at the project root rather than
mirroring — match whichever convention the project you're editing already uses.)

## Layer responsibilities

| Project | Responsibility | Depends on |
|---|---|---|
| `Notifications.Abstractions` | Data contracts + distributed-event contract shared by everyone | Nothing (in this repo) |
| `Notifications` (Core) | Definitions, publish/distribute pipeline, `INotificationStore` abstraction | Abstractions |
| `Notifications.Identity` | Permission-checker implementation | Core, ABP Identity |
| `Notifications.Emailing` / `.SignalR` | Notifier plugins | Abstractions (SignalR); Core (Emailing — see note below) |
| `NotificationCenter.Domain.Shared` | Constants, enums | Nothing |
| `NotificationCenter.Domain` | Aggregates (`Notification`, `UserNotification`, `NotificationSubscription`) | Domain.Shared, Core (`Notifications`) |
| `NotificationCenter.Application.Contracts` | DTOs, service interfaces | Domain.Shared, `Notifications.Abstractions` |
| `NotificationCenter.Application` | AppService implementations | Application.Contracts, Domain |
| `NotificationCenter.HttpApi` | Auto API Controllers | Application.Contracts |
| `NotificationCenter.HttpApi.Client` | Remote C# client proxies | Application.Contracts |
| `NotificationCenter.EntityFrameworkCore` / `.MongoDB` | `INotificationStore` implementations | Domain |

> **Plugin boundary**: the design intent (`docs/02-architecture.md`) is that Notifiers depend on
> **only** `Abstractions`, so any channel can be added without touching Core. `SignalR` follows
> this. `Emailing` currently also depends on Core (`Notifications`) — be aware of this when adding
> a new Notifier; prefer depending on Abstractions only unless you have a concrete reason to need
> Core's types.

## Two operation modes — both must keep working

1. **Stateless forwarding**: install `Notifications` + one or more Notifiers. No persistence —
   `NullNotificationStore` is used, target `UserIds` must be explicit, there's no subscription/inbox.
2. **Full Notification Center**: also install `NotificationCenter` (+ `EntityFrameworkCore` or
   `MongoDB`). Adds persistence, subscriptions, inbox, read/unread state, and the REST API.

New features must not assume the Center is installed unless they genuinely require persistence —
core publish/distribute logic has to work with `NullNotificationStore` too.

## Adding a feature

**A new notification type** (most common change — doesn't touch the Domain layer at all):
1. Define a `NotificationData` subclass wherever the business logic that raises it lives (or in
   `Abstractions` if it's shared across modules), tagged with a **stable, short discriminator** via
   `[NotificationDataType("...")]` — never rely on the CLR type name. See
   `.claude/rules/framework/common/notifications-invariants.md`.
2. Register it through an `INotificationDefinitionProvider` (name, display text, optional
   feature/permission gating, allowed channels).
3. Publish via `INotificationPublisher`. No entity/EF/Mongo changes needed — `Notification` /
   `UserNotification` are generic containers for any `NotificationData`.

**A new Notifier** (new channel, e.g. WebPush/FCM/SMS):
1. New project `Dignite.Abp.Notifications.<Channel>` under `core/src/`, depending on
   `Notifications.Abstractions` (only, if possible).
2. Implement `IDistributedEventHandler<RealTimeNotifyEto>` (and prefer the explicit
   `INotificationNotifier` contract where one exists) to relay to the channel's SDK.
3. Module class with `[DependsOn(typeof(AbpNotificationsAbstractionsModule), ...)]`.

**A new aggregate/entity in `NotificationCenter`** (rare — most needs are covered by
`NotificationData`, not new entities):
1. Entity in `NotificationCenter.Domain` (namespace-mirrored path), `BasicAggregateRoot<Guid>` +
   `IMultiTenant`, protected setters + behavior methods (see `ddd-patterns.md`).
2. Constants in `NotificationCenter.Domain.Shared`.
3. EF configuration in `NotificationCenterDbContextModelCreatingExtensions.ConfigureNotificationCenter()`
   and the equivalent Mongo mapping — **no migration to generate here**; see `ef-core.md`.
4. DTOs + service interface in `Application.Contracts`; implementation in `Application` using the
   generic `IRepository<T, Guid>` (this repo does not define custom per-aggregate repository
   interfaces — see `ddd-patterns.md`).
5. Tests in the matching `test` project — see `framework/testing/patterns.md` for this repo's
   actual naming convention (not the generic ABP `Should_X_When_Y` style).

## Commands

```bash
dotnet build Dignite.Abp.Notifications.slnx
dotnet build Dignite.Abp.NotificationCenter.slnx
dotnet test Dignite.Abp.Notifications.slnx
dotnet test Dignite.Abp.NotificationCenter.slnx
```

No `DbMigrator`, no `appsettings.json` — a consuming host owns its own DbContext/migrations. EF
Core integration tests run against an in-memory Sqlite provider, so `dotnet test` needs no migration
step.

## Docs

`docs/` carries the design rationale behind these rules — read before large architectural changes:

| Doc | Content |
|---|---|
| `docs/01-strategy.md` | Positioning, differentiation, naming decisions |
| `docs/02-architecture.md` | Layering, dependency diagram, the two modes, the publish→distribute→notify flow, extension points |
| `docs/03-roadmap.md` | Problems (A–F) found in the **legacy reference implementation** this repo replaces, and the priority plan (P0–P4) that produced today's invariants |

`03-roadmap.md` describes the *old* codebase being replaced, not necessarily open work here —
cross-check against `git log` before treating an item as unfinished (e.g. the P0 serialization
fix, the transactional outbox, and P2 notifier/channel-routing already exist in this repo).

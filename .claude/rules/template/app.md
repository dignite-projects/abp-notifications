# Dignite.Abp.Notifications — Solution Structure

> **Docs**: https://abp.io/docs/latest/solution-templates/module-development-template

The **distributed packages** are class libraries only — two independently distributable ABP module trees
(`core/` and `notification-center/`) under a **single** `.slnx` solution. There is no *production* `Host`,
`DbMigrator`, or frontend to publish; a real consuming application (not in this repo) references these
projects (or their NuGet packages) and owns the running app. The repo *does* carry a **local-dev-only** demo
`host/` (a runnable ABP MVC host — **in the `.slnx`** per ABP's module-template convention, but a Web SDK
project so it's never packed) plus an `angular/` workspace (npm-only, not in the `.slnx`), purely to
run/demo the stack. "Independently distributable" is enforced by project references + per-project NuGet
packaging (Core never references NotificationCenter), not by the solution file — the single solution is only
a build/dev convenience.

## Solution structure

```
abp-notifications/
├── Dignite.Abp.Notifications.slnx           # one solution: core/ + notification-center/ libs + demo host/
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
│   │   ├── Dignite.Abp.Notifications.Emailing.Identity/ # Email address resolver via ABP Identity
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
    └── test/
        ├── Dignite.Abp.NotificationCenter.TestBase/       # shared provider-agnostic test scenarios (abstract *_Tests<TModule>)
        ├── Dignite.Abp.NotificationCenter.Tests/          # EF Core / in-memory Sqlite provider + EF-only outbox test
        └── Dignite.Abp.NotificationCenter.MongoDB.Tests/  # MongoDB provider (embedded mongod via MongoSandbox)
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
| `Notifications.Emailing` / `.SignalR` | Notifier plugins | Abstractions + channel SDK |
| `Notifications.Emailing.Identity` | Email address resolver implementation | Emailing, ABP Identity |
| `NotificationCenter.Domain.Shared` | Constants, enums | Nothing |
| `NotificationCenter.Domain` | Aggregates (`Notification`, `UserNotification`, `NotificationSubscription`) | Domain.Shared, Core (`Notifications`) |
| `NotificationCenter.Application.Contracts` | DTOs, service interfaces | Domain.Shared, `Notifications.Abstractions` |
| `NotificationCenter.Application` | AppService implementations | Application.Contracts, Domain |
| `NotificationCenter.HttpApi` | Auto API Controllers | Application.Contracts |
| `NotificationCenter.HttpApi.Client` | Remote C# client proxies | Application.Contracts |
| `NotificationCenter.EntityFrameworkCore` / `.MongoDB` | `INotificationStore` implementations | Domain |

> **Plugin boundary**: the design intent is that Notifiers depend on
> **only** `Abstractions` and their own channel SDK, so any channel can be added without touching Core.
> Optional host-specific address mapping belongs in a separate integration package, as
> `Notifications.Emailing.Identity` does for ABP Identity.

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
dotnet test Dignite.Abp.Notifications.slnx

# Core only (skips the embedded-mongod MongoDB provider tests):
dotnet test core/test/Dignite.Abp.Notifications.Tests
```

No `DbMigrator`, no `appsettings.json` — a consuming host owns its own DbContext/migrations. EF
Core integration tests run against an in-memory Sqlite provider and the MongoDB provider tests run
against an embedded mongod (MongoSandbox), so `dotnet test` needs no migration step and no local
database install.

## Design rationale

The architecture overview (layering, the dependency diagram, the two modes, the
publish→distribute→notify flow, extension points) and usage live in the root `README.md`. The
"why" behind these rules — the serialization, DI-lifetime, plugin-boundary, and recipient-privacy
invariants — lives inline in `framework/common/notifications-invariants.md`.

This repo is a from-scratch rewrite of a legacy implementation; those invariants encode the exact
bugs that rewrite set out to fix, so don't reintroduce them.

# Dignite.Abp.Notifications

An extensible, event-driven notification framework for **ABP Framework** (LGPL-3.0), plus an
optional headless **Notification Center** (inbox / subscriptions / read-unread / REST API). This
repo is **class libraries only** — no `Host`, no `DbMigrator`, no frontend. A separate host
application (not in this repo) references these projects/packages and runs the actual app.

## Tech stack

- **.NET 10**, **ABP Framework 10.5.0**.
- **Central package management** (`Directory.Packages.props`) — every package version is pinned
  there; a `.csproj` only ever has `<PackageReference Include="..." />` with no `Version=`.
- Contract layers (`*.Abstractions`, `*.Domain.Shared`, `*.Application.Contracts`,
  `*.HttpApi.Client`) multi-target `netstandard2.0;netstandard2.1;net10.0`; implementation layers
  target `net10.0` only.
- Persistence: EF Core and MongoDB, both implementing the same `INotificationStore` abstraction.
- Tests: xUnit + Shouldly + NSubstitute + `Volo.Abp.TestBase` (Autofac); EF Core tests run against
  in-memory Sqlite.
- License: LGPL-3.0-only.

## Solution layout

Two sibling `.slnx` solutions, each its own module tree:

- **`Dignite.Abp.Notifications.slnx`** — the core framework: `core/src/{Abstractions, Notifications,
  Notifications.Identity, Notifications.Emailing, Notifications.SignalR}` + `core/test`.
- **`Dignite.Abp.NotificationCenter.slnx`** — optional persistence + REST API, depends on Core:
  `notification-center/src/{Domain.Shared, Domain, Application.Contracts, Application, HttpApi,
  HttpApi.Client, EntityFrameworkCore, MongoDB}` + `notification-center/test`.

Source files live at `<Project>/<mirrored namespace path>/File.cs` (every `.csproj` sets
`<RootNamespace />` empty) — not a generic `Entities/`/`Services/` split.

## Coding rules

Detailed conventions live in `.claude/rules/` and load automatically:

- `framework/common/abp-core.md`, `framework/common/notifications-invariants.md`, and
  `template/app.md` are **always loaded** (core ABP conventions + this repo's hard architectural
  invariants + the full solution map / "add a feature" flow).
- Everything else is **path-scoped** via `paths:` frontmatter — e.g. DDD patterns for
  `*.Domain/**/*.cs`, EF Core for `*DbContext*.cs`, tests for `test/**`.

Read `.claude/rules/template/app.md` first for the layer map and the "add a feature" flow, then
`.claude/rules/framework/common/notifications-invariants.md` before touching `NotificationData`,
any Notifier, or a service's DI lifetime — those invariants exist because violating them is
exactly what this rewrite was started to fix (see `docs/03-roadmap.md`).

## Commands

```bash
# Build / test (from repo root — there is no single top-level .sln)
dotnet build Dignite.Abp.Notifications.slnx
dotnet build Dignite.Abp.NotificationCenter.slnx
dotnet test Dignite.Abp.Notifications.slnx
dotnet test Dignite.Abp.NotificationCenter.slnx

# Pack for local testing (version/license come from Directory.Build.props)
dotnet pack Dignite.Abp.Notifications.slnx -c Release
```

No `DbMigrator`, no `appsettings.json` here — a consuming host owns its own DbContext/migrations
and calls `ConfigureNotificationCenter(builder)` (or implements `INotificationCenterDbContext`)
from its own `OnModelCreating`. Tests run against in-memory Sqlite, so `dotnet test` needs no
migration step.

## Core conventions (see rules for the full picture)

- Respect ABP DDD layer boundaries: no `DbContext` in Application, DTOs at boundaries.
- Entities (`Notification`, `UserNotification`, `NotificationSubscription`) are
  `BasicAggregateRoot<Guid>` + explicit `IMultiTenant`, with protected setters and behavior
  methods. No custom per-aggregate repository interfaces — queries go through the generic
  `IRepository<T, Guid>` inside `INotificationStore`.
- `NotificationData` serialization must use a stable `[NotificationDataType]` discriminator via
  System.Text.Json only — never a CLR type name/`AssemblyQualifiedName`, never Newtonsoft.
- Prefer base-class properties (`Clock`, `CurrentUser`, `GuidGenerator`, `L`) over injecting them —
  but note plain (non-ABP-base-class) services in this repo, like `NotificationStore`, correctly
  inject these instead.
- New package version pins go in `Directory.Packages.props`, never inline in a `.csproj`.
- Core (`Notifications`) must keep working with `NullNotificationStore` alone — don't assume
  `NotificationCenter` is installed.

## Docs

`docs/` carries the design rationale — skim before large changes: `docs/01-strategy.md`
(positioning, naming decisions), `docs/02-architecture.md` (layering, the two operation modes, the
publish→distribute→notify flow), `docs/03-roadmap.md` (problems found in the legacy implementation
this repo replaces, and the priority plan that produced today's invariants — historical rationale,
not a live bug tracker; cross-check against `git log` before assuming an item is still open).

<!-- .claude/rules/ adapted from D:\dignite-studio\astar-insatsu — that repo's frontend/product rules were dropped as not applicable here (this repo has no frontend and is itself the product). -->

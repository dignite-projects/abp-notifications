---
paths:
  - "**/*.csproj"
  - "Directory.Packages.props"
  - "Directory.Build.props"
---

# Build, Test & Package Commands

> **ABP CLI docs**: https://abp.io/docs/latest/cli — most ABP CLI commands (`generate-proxy`,
> `install-libs`, `suite generate`) don't apply here: this repo has no frontend and no `Host` to
> point them at. What's actually relevant day to day is plain `dotnet` plus this repo's central
> package management.

## Build / test

```bash
# Per solution (there is no single top-level .sln)
dotnet build Dignite.Abp.Notifications.slnx
dotnet build Dignite.Abp.NotificationCenter.slnx

dotnet test Dignite.Abp.Notifications.slnx
dotnet test Dignite.Abp.NotificationCenter.slnx

# A single test project
dotnet test notification-center/test/Dignite.Abp.NotificationCenter.Tests
```

## Central package management — adding/updating a NuGet dependency

All versions live in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). To add a
dependency to a project:

1. Add (or confirm) a `<PackageVersion Include="Pkg.Name" Version="x.y.z" />` line in
   `Directory.Packages.props`, grouped under the matching `<ItemGroup Label="...">`.
2. Reference it in the project's `.csproj` with **no version**: `<PackageReference Include="Pkg.Name" />`.

Never put a `Version=` on a `<PackageReference>` inside a `.csproj` — that defeats central package
management and will drift from the pinned version.

## Project references between the two module trees

`NotificationCenter` projects reference Core projects directly, e.g.:

```xml
<ProjectReference Include="..\..\..\core\src\Dignite.Abp.Notifications\Dignite.Abp.Notifications.csproj" />
```

`abp add-package-ref` can do this for you and also wires the module `[DependsOn(...)]` if you
prefer the CLI over hand-editing:

```bash
abp add-package-ref Dignite.Abp.Notifications -t notification-center/src/Dignite.Abp.NotificationCenter.Domain/Dignite.Abp.NotificationCenter.Domain.csproj
```

## Packaging (NuGet)

Version, license, and package metadata come from the root `Directory.Build.props`
(`<Version>`, `PackageLicenseExpression` = `LGPL-3.0-only`, `PackageProjectUrl`). To build local
packages for testing:

```bash
dotnet pack Dignite.Abp.Notifications.slnx -c Release
dotnet pack Dignite.Abp.NotificationCenter.slnx -c Release
```

Bump `<Version>` in `Directory.Build.props` before a real release — it applies to every project in
the repo (there's no per-project versioning).

## Not applicable in this repo

- `abp generate-proxy` (JS/C#/Angular) — no frontend, no `Host` to point it at.
- `abp install-libs` — no MVC/Blazor UI here.
- `abp suite generate` — no `.suite/entities/` in this repo.
- Anything involving a `DbMigrator` — this repo doesn't have one; see `framework/data/ef-core.md`.

# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html) — with one
deviation from the classic scheme: see [CONTRIBUTING.md → Versioning and releases](CONTRIBUTING.md#versioning-and-releases)
for why `MAJOR` tracks the targeted ABP Framework version rather than this module's own breaking
changes.

## [Unreleased]

### Added

- Added scoped subscription application/REST contracts that round-trip the stable entity type and ID,
  while retaining the name-only methods as definition-wide compatibility wrappers for callers. MVC and
  Angular subscription UIs now submit the complete scope.
- Added a replaceable, batch-shaped `INotificationRecipientEligibilityEvaluator` shared by explicit and
  subscription-derived recipients, plus a narrowly named and warning-logged trusted-system bypass that is
  restricted to explicit recipients.
- Added opt-in notification-definition contracts for stable payload discriminators and forbidden/optional/required
  entity identity, including optional stable entity type constraints and startup validation of referenced payloads.
- Added explicit notification payload schema versions, deterministic consecutive JSON upcasters with startup chain
  validation, typed strict-read failures, and a safe `UnsupportedNotificationData` tolerant-read representation.
  MVC and Angular now render unsupported payloads with a localized fallback.
- Added a configurable bounded recipient pipeline for candidate resolution, eligibility, inbox multi-inserts, and
  delivery-event publication. `IBatchedNotificationStore`, `ICancellableNotificationDistributor`, and
  `IPreparedNotificationDistributor` are additive capability contracts, and `NotificationDistributionMetrics`
  publishes stable candidate/eligible/filtered/batch/duration/failure instruments. Shared EF Core/MongoDB tests
  cover 2,001 recipients, duplicate scopes, keyset changes, exact limits, cancellation, and independently scheduled
  explicit batches.

### Changed

- **Breaking for implementers.** `INotificationAppService` gained scoped subscribe/unsubscribe members;
  custom implementations and replacements must implement the new methods and be recompiled. Existing
  callers of the name-only members remain source-compatible.
- Subscription-driven distribution now treats a definition-wide subscription as a fallback for every
  entity and combines it with an exact entity subscription without delivering twice to the same user.
- Notification subscription uniqueness now uses non-null, ordinal identity keys across EF Core and
  MongoDB. Existing databases require a host-owned backfill and index migration as documented in the
  README; this repository does not ship consumer migrations.
- **Breaking behavior for callers.** Explicit `userIds` no longer bypass a notification definition's
  permission and feature requirements: `PublishAsync` now filters explicit and subscription-derived
  recipients through the same policy in the notification's tenant or host context. Call the named
  `PublishToExplicitRecipientsWithoutEligibilityChecksAsync` API only for mandatory trusted-system delivery.
- **Breaking for implementers.** `INotificationPublisher` and `INotificationDistributor` gained the named
  explicit-recipient bypass members. Custom implementations must add them and be recompiled. Existing
  `DefaultNotificationDistributor` subclasses overriding its legacy protected selection/persistence/publication
  hooks remain active through a compatibility pipeline, after which the shared eligibility policy is applied.
  That compatibility path is intentionally materializing; migrate the customization to
  `IBatchedNotificationStore` and `INotificationRecipientEligibilityEvaluator` for bounded fan-outs.
- **Breaking for manual construction.** The three-argument `DefaultNotificationDistributor` constructor was
  removed because it could neither establish the notification's tenant/host context nor guarantee bypass audit
  logging. Manual callers must now supply `INotificationRecipientEligibilityEvaluator`, `ICurrentTenant`, and
  `ILogger<DefaultNotificationDistributor>`; normal dependency-injection resolution requires no changes.
- **Breaking behavior for direct distributor callers.** `NotificationInfo.TenantId` is now authoritative for
  subscription lookup, eligibility, persistence, and event/outbox publication. `null` always means host and no
  longer falls back to an ambient tenant, so tenant-side callers of `INotificationDistributor` must set it explicitly.
- **Breaking for manual construction.** `DefaultNotificationPublisher` now requires
  `INotificationDefinitionManager` and `INotificationDataTypeRegistry`, and `DefaultNotificationDistributor` also
  requires `INotificationDataTypeRegistry`, so both the pre-enqueue and persistence/event boundaries validate
  definition contracts. Normal dependency-injection resolution requires no changes. Definitions without
  payload/entity contracts retain their previous permissive behavior and can migrate independently.
- Newly serialized notification payloads always include `schemaVersion`; versionless historical JSON is read as
  schema v1 and upgraded lazily without a database migration or eager row rewrite. Notification Center store
  queries and distributed-event/HTTP converters now tolerate unknown, future, malformed, and failed-upcast payloads
  by returning safe placeholder data, while `INotificationDataSerializer.Deserialize` retains strict behavior.
- Distribution can now publish multiple `NotificationDeliveryEto` work items for one notification. Built-in EF Core,
  MongoDB, and null stores use keyset pages and bounded writes; large explicit fan-outs prepare the notification once
  and enqueue bounded recipient jobs. Explicit normalization uses bounded keyset windows rather than a
  notification-wide duplicate set, and the inline threshold now shares the 10,000 hard maximum. EF Core flushes and
  detaches each inbox group while retaining an ambient transaction when one exists. Existing custom
  `INotificationStore` implementations remain compatible through the legacy materializing/per-row path and should
  add the new capabilities before large fan-outs. The original four-parameter `NotificationDistributionJobArgs`
  constructor remains available for binary compatibility. No database migration or delivery-ledger/retry policy is
  included.
- **Breaking behavior for payload authors.** Per-instance assignments to `NotificationData.SchemaVersion` no longer
  select the wire version. The converter now writes the current version declared by
  `[NotificationDataType("...", version)]`; move shape changes to that declaration and registered upcasters.

### Fixed

- Notification definition names and `NotificationData` discriminators now use explicit ordinal,
  case-sensitive registration and lookup. Conflicting registrations fail during application startup
  with both providers or CLR mappings identified instead of silently replacing an earlier value. Definition
  providers are discovered across module assemblies and duplicate registrations of the same provider execute once.
- Defined consistent explicit-recipient semantics across inline and background distribution: `null`
  resolves subscriptions, an empty list is a no-op, and duplicate explicit user IDs are normalized
  before threshold selection, persistence, and channel delivery.

## [10.0.0-rc.2] - 2026-07-16

### Changed

- Renamed the Angular package to `@dignite/abp.ng.notification-center`, making the UI framework
  explicit and leaving room for parallel React or other client packages.
- Clarified that npm requires every package to have a `latest` dist-tag, so a package whose first
  public version is a pre-release temporarily exposes that version as both `next` and `latest`.

## [10.0.0-rc.1] - 2026-07-16

### Added

- Added CI coverage for the Angular library and production demo build.
- Added isolated consumption smoke tests for all 15 packed NuGet packages and both Angular package
  entry points.

### Changed

- Synchronized the Angular package version with the NuGet release version.
- Renamed the Angular package to `@dignite/abp-notification-center` so all Dignite npm packages
  share the `@dignite` organization scope.
- Tagged pre-releases are now published publicly to NuGet.org and npm in addition to the existing
  GitHub Packages preview feed.
- Replaced the long-lived NuGet API key with NuGet.org Trusted Publishing and a short-lived OIDC
  credential issued to the tagged release workflow.
- Expanded the README with package installation commands, a compatibility table, and migration
  guidance for legacy 3.x consumers.

## [10.0.0-preview.2] - 2026-07-10

> `MAJOR` tracks the targeted ABP Framework version, so a breaking change to this module's own
> contracts arrives in a `MINOR` bump. Entries below marked **Breaking** require action when
> upgrading.

### Added

- Added `Dignite.Abp.Notifications.Emailing.Identity`, an optional ABP Identity-backed
  `IEmailNotificationAddressResolver` for the Emailing notifier.
- `NotificationDeliveryEto` and `NotificationDelivery` now carry the notification's `EntityTypeName` and
  `EntityId`, so a notifier can identify the business entity a notification is about without depending on
  Core.
- Added `NotificationEmailContentProvider<TData>`, a base class that narrows `NotificationData` once so an
  implementer cannot forget the type guard and accidentally claim every notification.
- Email address resolvers can now return an optional recipient culture; email content is built inside that culture
  and falls back to `NotificationEmailOptions.DefaultCulture`.

### Changed

- Changed email address resolution to use `EmailNotificationAddressResolveContext`, making
  `TenantId` explicit to local and remote resolver implementations.
- **Breaking.** `NotificationEntityIdentifier` now takes `(string entityTypeName, string entityId)` instead of
  `(Type entityType, object entityId)` — pass a short, stable name such as `"Demo.Order"`. Persisted
  `EntityTypeName` values are therefore no longer `Type.FullName`, so subscription rows and
  `NotificationCenterWebOptions.EntityLinkResolvers` keys written in the old format stop matching.
- **Breaking.** `IEmailNotificationAddressResolver` gained an `Order` member, and `GetEmailOrNullAsync` returns
  `Task<EmailNotificationAddress?>` rather than `Task<string?>`. Resolvers now form an ordered chain in which the
  first non-null address wins.
- **Breaking.** `IdentityEmailNotificationAddressResolver` no longer declares `[Dependency(ReplaceServices = true)]`.
  It joins the chain at `NotificationEmailProviderOrders.BuiltInFallback`, so an application resolver composes with
  it rather than displacing it.
- **Breaking.** Renamed `NotificationEmailContentProviderOrders` to `NotificationEmailProviderOrders`, which now
  orders both the content-provider and the address-resolver chains.
- **Breaking.** `NotificationEmailBuildContext`'s constructor gained a `cultureName` parameter.
- Documented that atomic persist-and-publish, and deduplication of a redelivered event, require the host to enable
  ABP's transactional outbox — `UseNotificationCenterEfCoreOutbox()` on EF Core. The MongoDB provider wires neither.
  No behaviour changed; the previous comments and README described a guarantee that was conditional.

### Fixed

- `EmailNotifier` no longer aborts the whole delivery when a recipient's culture name cannot be parsed. It falls
  back to `NotificationEmailOptions.DefaultCulture` and then to the ambient culture, logging a warning for each
  rejected value. Previously a single malformed per-user language setting threw out of the event handler, so every
  recipient after it received nothing and a redelivery re-mailed the ones before it. As part of this,
  `NotificationEmailBuildContext.CultureName` now accepts the empty string, which is the invariant culture.

### Removed

- **Breaking.** Removed `NullEmailNotificationAddressResolver` — an empty resolver chain already resolves no
  address.

## [10.0.0-preview.1] - 2026-07-09

### Added

- Initial public release: an event-driven, pluggable notification framework for ABP Framework
  (`core/`), plus an optional Notification Center providing a persistent inbox, subscriptions,
  read/unread state, and a REST API, with MVC and Angular UI libraries
  (`notification-center/`).
- Dual persistence support (EF Core and MongoDB) behind a shared `INotificationStore` abstraction.
- Real-time push (SignalR) and email notifiers.
- Optional permission gating for notification definitions via ABP Identity.

# Issue #60 recipient batching measurements

These measurements exercise the same provider-agnostic integration test against both shipped stores. The scenario:

- inserts 2,001 distinct subscribers;
- gives 301 of them both a definition-wide and exact-entity subscription;
- distributes one entity notification;
- verifies exactly 2,001 inbox rows and recipient IDs;
- uses candidate/write/event limits of 256/128/100.

## Reproduce

Build once, then run each command three times from the repository root:

```powershell
dotnet build Dignite.Abp.Notifications.slnx

1..3 | ForEach-Object {
    Measure-Command {
        dotnet test notification-center/test/Dignite.Abp.NotificationCenter.Tests/Dignite.Abp.NotificationCenter.Tests.csproj `
            --no-build --no-restore `
            --filter "FullyQualifiedName~Thousands_of_duplicate_scope_subscribers" `
            --logger "console;verbosity=quiet"
    }
}

1..3 | ForEach-Object {
    Measure-Command {
        dotnet test notification-center/test/Dignite.Abp.NotificationCenter.MongoDB.Tests/Dignite.Abp.NotificationCenter.MongoDB.Tests.csproj `
            --no-build --no-restore `
            --filter "FullyQualifiedName~Thousands_of_duplicate_scope_subscribers" `
            --logger "console;verbosity=quiet"
    }
}
```

The elapsed time includes test-host startup, database setup, 2,302 subscription inserts, distribution, and
assertion queries. It is a reproducible regression workload, not a provider throughput microbenchmark.

## Recorded result

Recorded 2026-07-17 at commit base `307b161`, .NET SDK 10.0.302, Windows 10.0.26200, Intel Core i7-8700,
31.9 GB RAM:

| Provider | Run 1 | Run 2 | Run 3 | Median |
|---|---:|---:|---:|---:|
| EF Core / in-memory SQLite | 7,185 ms | 8,126 ms | 8,868 ms | 8,126 ms |
| MongoDB / MongoSandbox | 8,221 ms | 8,370 ms | 9,542 ms | 8,370 ms |

Both providers completed with the same 2,001-recipient result and 21 delivery events. No provider-specific bulk
extension was required.

## Default-size rationale

- `RecipientBatchSize = 256` bounds eligibility and transient candidate collections while requiring only eight
  pages for this workload.
- `UserNotificationWriteBatchSize = 256` keeps a typical relational insert comfortably below common 2,100
  parameter limits even when an inbox row uses several bound values, while still mapping to one MongoDB
  `InsertMany` unit. Hosts with wider provider mappings can lower it independently.
- `DeliveryEventRecipientLimit = 100` makes broker growth independent of database tuning. One hundred textual GUIDs
  are roughly 3.6 KB before JSON framing and notification data, leaving useful headroom on common broker limits.

All three defaults are independent because database, policy, and transport constraints differ. The hard maximum
of 10,000 prevents accidental configuration from recreating notification-wide units.

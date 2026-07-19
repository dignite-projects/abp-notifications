# Issue #60 recipient batching measurements

These measurements exercise two provider-agnostic integration tests against both shipped stores.

The subscription scenario:

- inserts 2,001 distinct subscribers;
- gives 301 of them both a definition-wide and exact-entity subscription;
- distributes one entity notification;
- verifies exactly 2,001 inbox rows and recipient IDs;
- uses the default candidate/write/event limits of 256/256/100.

The explicit scenario:

- supplies 2,001 distinct IDs plus four duplicates that cross normalization windows;
- prepares one notification and creates eight jobs carrying at most 256 IDs each;
- executes the jobs in reverse order to prove they are independent;
- verifies exactly 2,001 inbox rows and recipient IDs and delivery events no larger than 100;
- uses the same default 256/256/100 limits.

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

1..3 | ForEach-Object {
    Measure-Command {
        dotnet test notification-center/test/Dignite.Abp.NotificationCenter.Tests/Dignite.Abp.NotificationCenter.Tests.csproj `
            --no-build --no-restore `
            --filter "FullyQualifiedName~Large_explicit_publish_prepares_once" `
            --logger "console;verbosity=quiet"
    }
}

1..3 | ForEach-Object {
    Measure-Command {
        dotnet test notification-center/test/Dignite.Abp.NotificationCenter.MongoDB.Tests/Dignite.Abp.NotificationCenter.MongoDB.Tests.csproj `
            --no-build --no-restore `
            --filter "FullyQualifiedName~Large_explicit_publish_prepares_once" `
            --logger "console;verbosity=quiet"
    }
}
```

The elapsed time includes test-host startup, database setup, scenario setup, distribution, and assertion queries.
The subscription case includes 2,302 subscription inserts. These are reproducible regression workloads, not
provider throughput microbenchmarks. The EF-only
`Ef_batches_flush_and_detach_inbox_entities_inside_one_transaction` test separately verifies the memory boundary
that elapsed time cannot show: after distributing 513 recipients with the default 256 write size, zero
`UserNotification` entities remain tracked before commit.

## Recorded result

Recorded 2026-07-17 at commit `90b36e7`, .NET SDK 10.0.302, Windows 10.0.26200, Intel Core i7-8700,
31.9 GB RAM:

| Scenario | Provider | Run 1 | Run 2 | Run 3 | Median |
|---|---|---:|---:|---:|---:|
| Subscription | EF Core / in-memory SQLite | 6,371 ms | 7,304 ms | 7,536 ms | 7,304 ms |
| Explicit | EF Core / in-memory SQLite | 6,633 ms | 7,199 ms | 6,752 ms | 6,752 ms |
| Subscription | MongoDB / MongoSandbox | 8,051 ms | 7,669 ms | 8,012 ms | 8,012 ms |
| Explicit | MongoDB / MongoSandbox | 7,131 ms | 6,669 ms | 6,277 ms | 6,669 ms |

Both providers completed each scenario with the same 2,001-recipient result. Subscription distribution produced
21 delivery events; the eight independently executed explicit jobs produced 24 because each job flushes its own
final partial event. No provider-specific bulk-extension dependency is used; the EF package supplies only its own
flush-and-detach adapter.

## Default-size rationale

- `RecipientBatchSize = 256` bounds eligibility and transient candidate collections while requiring only eight
  pages for these workloads. Explicit normalization rescans the caller-owned array once per page while retaining
  only one 256-ID sorted window; the recorded 2,001-recipient medians include those eight scans.
- `UserNotificationWriteBatchSize = 256` keeps a typical relational insert comfortably below common 2,100
  parameter limits even when an inbox row uses several bound values, while still mapping to one MongoDB
  `InsertMany` unit. Hosts with wider provider mappings can lower it independently.
- `DeliveryWorkItemBatchSize = 100` bounds the number of single-recipient/channel work items scheduled by one
  operation independently from database tuning. Each broker event still carries exactly one recipient and channel.

All three defaults are independent because database, policy, and transport constraints differ. The hard maximum
of 10,000 prevents accidental configuration from recreating notification-wide units.

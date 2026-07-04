---
paths:
  - "test/**/*.cs"
  - "tests/**/*.cs"
  - "**/*Tests*/**/*.cs"
  - "**/*Test*.cs"
---

# Testing Conventions in This Repo

> **ABP testing docs**: https://abp.io/docs/latest/testing

Stack: **xUnit** + **Shouldly** + **NSubstitute** + `Volo.Abp.TestBase` (Autofac). EF Core
integration tests run against an in-memory **Sqlite** provider — no real database needed.

## Test naming: descriptive sentences, NOT `Should_X_When_Y`

The generic ABP template convention (`Should_Create_Book_When_Input_Is_Valid`) is **not** what
this repo actually uses. Match the existing style — a plain-English sentence, snake_cased,
describing the observed behavior:

```csharp
// ✅ Actual convention used in this repo
public async Task Round_trips_custom_notification_data_through_the_store()
public async Task Persisted_data_uses_the_discriminator_and_no_assembly_qualified_name()

// ❌ Don't introduce the generic ABP-template style here
public async Task Should_Create_Book_When_Input_Is_Valid()
```

Test class naming still follows `{TypeUnderTest}_Tests` (e.g. `NotificationStore_Tests`,
`NotificationAppService_Tests`) — that part matches the generic ABP convention.

## Base classes

Both `core/test` and `notification-center/test` follow the same shape: an abstract `*TestBase`
inheriting `AbpIntegratedTest<TTestModule>`, using Autofac:

```csharp
public abstract class NotificationCenterTestBase : AbpIntegratedTest<NotificationCenterTestModule>
{
    protected override void SetAbpApplicationCreationOptions(AbpApplicationCreationOptions options)
    {
        options.UseAutofac();
    }

    // Helper used across this repo's tests — wrap direct store/repository calls in a UoW,
    // since (unlike an AppService call) resolving INotificationStore/IRepository directly
    // doesn't open one for you.
    protected virtual async Task WithUnitOfWorkAsync(Func<Task> func)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        await func();
        await uow.CompleteAsync();
    }

    // Helper for tests that need a specific authenticated user
    protected virtual IDisposable ChangeCurrentUser(Guid userId) { /* ... */ }
}
```

Concrete test classes inherit the relevant base (`NotificationCenterTestBase`,
`DigniteAbpNotificationsTestBase`) and resolve what they need via `GetRequiredService<T>()`.

## Explicit `WithUnitOfWorkAsync` when testing stores/repositories directly

Most tests here exercise `INotificationStore` or `IRepository<T, Guid>` directly (not through an
AppService, which would open its own UoW). Wrap those calls:

```csharp
[Fact]
public async Task Round_trips_custom_notification_data_through_the_store()
{
    var notificationId = Guid.NewGuid();
    var userId = Guid.NewGuid();

    await WithUnitOfWorkAsync(async () =>
    {
        var store = GetRequiredService<INotificationStore>();
        await store.InsertNotificationAsync(new NotificationInfo { /* ... */ });
    });

    await WithUnitOfWorkAsync(async () =>
    {
        var store = GetRequiredService<INotificationStore>();
        var list = await store.GetUserNotificationsAsync(userId);

        list.Count.ShouldBe(1);
        var data = list.Single().Notification.Data.ShouldBeOfType<OrderShippedNotificationData>();
        data.OrderNumber.ShouldBe("SO-1001");
    });
}
```

## Assertions — Shouldly

```csharp
result.ShouldNotBeNull();
result.Name.ShouldBe("Expected");
entity.Data.ShouldNotContain("Version=");           // e.g. asserting no AssemblyQualifiedName leaked
var data = notification.Data.ShouldBeOfType<MyNotificationData>();

await Should.ThrowAsync<BusinessException>(async () => await _service.DoSomethingAsync());
```

## Mocking — NSubstitute

```csharp
var emailSender = Substitute.For<IEmailSender>();
emailSender.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
    .Returns(Task.CompletedTask);
context.Services.AddSingleton(emailSender);
```

## Custom `NotificationData` for tests

When a test needs a concrete `NotificationData` subclass, define a small test-only one (see
`OrderShippedNotificationData` in `notification-center/test/.../OrderShippedNotificationData.cs`)
tagged with its own `[NotificationDataType("Test.OrderShipped")]` — don't reuse a production type
just because it's convenient, and don't skip the discriminator (that's exactly what
`notifications-invariants.md` §1 tests are meant to catch).

## General best practices (still apply)

- Each test independent; don't share mutable state between tests.
- Test edge cases and error conditions, not just the happy path.
- Prefer integration tests with real services over mocking internals — mock only true externals
  (email/SMS gateways, etc.).

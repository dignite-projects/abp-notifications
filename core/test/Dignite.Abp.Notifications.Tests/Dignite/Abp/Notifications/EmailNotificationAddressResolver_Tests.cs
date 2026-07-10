using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dignite.Abp.Notifications.Emailing;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Dignite.Abp.Notifications;

public class EmailNotificationAddressResolver_Tests
{
    private sealed class StubProvider : IEmailNotificationAddressProvider
    {
        private readonly Func<EmailNotificationAddressResolveContext, EmailNotificationAddress?> _result;

        public int Order { get; }
        public int Calls { get; private set; }

        public StubProvider(int order, Func<EmailNotificationAddressResolveContext, EmailNotificationAddress?> result)
        {
            Order = order;
            _result = result;
        }

        public Task<EmailNotificationAddress?> GetAddressOrNullAsync(EmailNotificationAddressResolveContext context)
        {
            Calls++;
            return Task.FromResult(_result(context));
        }
    }

    private static DefaultEmailNotificationAddressResolver Resolver(params IEmailNotificationAddressProvider[] providers)
    {
        return new DefaultEmailNotificationAddressResolver(
            providers,
            NullLogger<DefaultEmailNotificationAddressResolver>.Instance);
    }

    private static EmailNotificationAddressResolveContext Context()
    {
        return EmailingIdentityProvider_Tests.CreateContext(Guid.NewGuid(), entityTypeName: "Demo.Order", entityId: "1001");
    }

    [Fact]
    public async Task Lower_order_provider_wins_and_later_ones_are_not_consulted()
    {
        var first = new StubProvider(EmailNotificationAddressProviderOrders.Default,
            _ => EmailNotificationAddress.To("order@example.com"));
        var fallback = new StubProvider(EmailNotificationAddressProviderOrders.BuiltInFallback,
            _ => EmailNotificationAddress.To("account@example.com"));

        // Registration order deliberately reversed: Order, not DI order, decides.
        (await Resolver(fallback, first).GetEmailOrNullAsync(Context())).ShouldBe("order@example.com");

        first.Calls.ShouldBe(1);
        fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_null_result_passes_to_the_next_provider()
    {
        var passes = new StubProvider(EmailNotificationAddressProviderOrders.Default, _ => null);
        var fallback = new StubProvider(EmailNotificationAddressProviderOrders.BuiltInFallback,
            _ => EmailNotificationAddress.To("account@example.com"));

        (await Resolver(passes, fallback).GetEmailOrNullAsync(Context())).ShouldBe("account@example.com");

        passes.Calls.ShouldBe(1);
        fallback.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task None_claims_the_notification_and_stops_the_identity_fallback()
    {
        // The opt-out case. With a two-state `string?` contract this provider could only return null, fall through,
        // and have the fallback mail the account address anyway — silently defeating the opt-out.
        var optedOut = new StubProvider(EmailNotificationAddressProviderOrders.Default,
            _ => EmailNotificationAddress.None);
        var fallback = new StubProvider(EmailNotificationAddressProviderOrders.BuiltInFallback,
            _ => EmailNotificationAddress.To("account@example.com"));

        (await Resolver(optedOut, fallback).GetEmailOrNullAsync(Context())).ShouldBeNull();

        optedOut.Calls.ShouldBe(1);
        fallback.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_empty_chain_resolves_no_address()
    {
        (await Resolver().GetEmailOrNullAsync(Context())).ShouldBeNull();
    }

    // Two distinct types at the same Order: "AaaProvider" sorts before "ZzzProvider" by FullName (Ordinal).
    private sealed class AaaProvider : IEmailNotificationAddressProvider
    {
        public int Order => EmailNotificationAddressProviderOrders.Default;
        public Task<EmailNotificationAddress?> GetAddressOrNullAsync(EmailNotificationAddressResolveContext context)
            => Task.FromResult<EmailNotificationAddress?>(EmailNotificationAddress.To("aaa@example.com"));
    }

    private sealed class ZzzProvider : IEmailNotificationAddressProvider
    {
        public int Order => EmailNotificationAddressProviderOrders.Default;
        public Task<EmailNotificationAddress?> GetAddressOrNullAsync(EmailNotificationAddressResolveContext context)
            => Task.FromResult<EmailNotificationAddress?>(EmailNotificationAddress.To("zzz@example.com"));
    }

    [Fact]
    public async Task Providers_of_equal_order_are_broken_by_type_name_so_the_chain_is_deterministic()
    {
        // Same tiebreak DefaultNotificationEmailBuilder uses. Registration order must not decide the winner,
        // otherwise which address a user receives depends on module load order.
        (await Resolver(new AaaProvider(), new ZzzProvider()).GetEmailOrNullAsync(Context()))
            .ShouldBe("aaa@example.com");

        (await Resolver(new ZzzProvider(), new AaaProvider()).GetEmailOrNullAsync(Context()))
            .ShouldBe("aaa@example.com");
    }
}

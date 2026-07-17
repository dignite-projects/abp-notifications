using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Builds and caches definition registries so startup validation and runtime lookup execute each provider once.
/// </summary>
internal static class NotificationDefinitionRegistry
{
    private static readonly ConditionalWeakTable<
        NotificationOptions,
        Lazy<IDictionary<string, NotificationDefinition>>> Registries = new();

    public static IDictionary<string, NotificationDefinition> GetOrCreate(
        NotificationOptions options,
        IServiceScopeFactory serviceScopeFactory)
    {
        Check.NotNull(options, nameof(options));
        Check.NotNull(serviceScopeFactory, nameof(serviceScopeFactory));

        return Registries
            .GetValue(options, value => new Lazy<IDictionary<string, NotificationDefinition>>(
                () => CreateDefinitions(value, serviceScopeFactory), isThreadSafe: true))
            .Value;
    }

    private static IDictionary<string, NotificationDefinition> CreateDefinitions(
        NotificationOptions options,
        IServiceScopeFactory serviceScopeFactory)
    {
        var context = new NotificationDefinitionContext();

        using (var scope = serviceScopeFactory.CreateScope())
        {
            foreach (var providerType in options.DefinitionProviders)
            {
                context.SetCurrentProvider(providerType);
                var provider = (INotificationDefinitionProvider)scope.ServiceProvider.GetRequiredService(providerType);
                provider.Define(context);
            }
        }

        return context.Definitions;
    }
}

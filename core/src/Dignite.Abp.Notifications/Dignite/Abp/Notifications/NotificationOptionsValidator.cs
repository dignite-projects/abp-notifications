using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Builds the definition registry during host startup so provider conflicts fail before notifications are used.
/// </summary>
[ExposeServices(typeof(IValidateOptions<NotificationOptions>))]
internal class NotificationOptionsValidator : IValidateOptions<NotificationOptions>, ITransientDependency
{
    protected IServiceScopeFactory ServiceScopeFactory { get; }

    public NotificationOptionsValidator(IServiceScopeFactory serviceScopeFactory)
    {
        ServiceScopeFactory = serviceScopeFactory;
    }

    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        try
        {
            NotificationDefinitionRegistry.GetOrCreate(options, ServiceScopeFactory);
            return ValidateOptionsResult.Success;
        }
        catch (Exception exception)
        {
            return ValidateOptionsResult.Fail($"Notification definition registration failed: {exception.Message}");
        }
    }
}

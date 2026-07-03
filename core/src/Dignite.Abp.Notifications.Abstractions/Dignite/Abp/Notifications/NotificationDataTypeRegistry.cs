using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Dignite.Abp.Notifications;

public class NotificationDataTypeRegistry : INotificationDataTypeRegistry, ISingletonDependency
{
    private readonly Dictionary<string, Type> _byDiscriminator;
    private readonly Dictionary<Type, string> _byType;

    public NotificationDataTypeRegistry(IOptions<NotificationDataOptions> options)
    {
        _byDiscriminator = new Dictionary<string, Type>();
        _byType = new Dictionary<Type, string>();

        foreach (var pair in options.Value.DataTypes)
        {
            _byDiscriminator[pair.Key] = pair.Value;
            _byType[pair.Value] = pair.Key;
        }
    }

    public string? GetDiscriminatorOrNull(Type dataType)
    {
        return _byType.TryGetValue(dataType, out var name) ? name : null;
    }

    public Type? GetTypeOrNull(string discriminator)
    {
        return _byDiscriminator.TryGetValue(discriminator, out var type) ? type : null;
    }
}

using System;
using System.Collections.Generic;
using Volo.Abp;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Registers the <see cref="NotificationData"/> types known to the application, keyed by their stable
/// discriminator. Business modules add their own types here so those types can be resolved on any client.
/// </summary>
public class NotificationDataOptions
{
    public IDictionary<string, Type> DataTypes { get; }

    public NotificationDataOptions()
    {
        DataTypes = new Dictionary<string, Type>();
    }

    public NotificationDataOptions Add<TData>() where TData : NotificationData
    {
        return Add(typeof(TData));
    }

    public NotificationDataOptions Add(Type dataType)
    {
        Check.NotNull(dataType, nameof(dataType));

        var name = NotificationDataTypeAttribute.GetNameOrNull(dataType)
            ?? throw new ArgumentException(
                $"'{dataType.FullName}' must be annotated with [NotificationDataType(\"...\")] to be registered.",
                nameof(dataType));

        return Add(name, dataType);
    }

    public NotificationDataOptions Add(string discriminator, Type dataType)
    {
        Check.NotNullOrWhiteSpace(discriminator, nameof(discriminator));
        Check.AssignableTo<NotificationData>(dataType, nameof(dataType));

        DataTypes[discriminator] = dataType;
        return this;
    }
}

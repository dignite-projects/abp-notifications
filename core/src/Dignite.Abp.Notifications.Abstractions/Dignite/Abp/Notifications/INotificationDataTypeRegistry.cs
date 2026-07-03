using System;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Single source of truth mapping stable discriminators to <see cref="NotificationData"/> CLR types.
/// Used by serialization on both the writing (server) and reading (remote client) sides.
/// </summary>
public interface INotificationDataTypeRegistry
{
    string? GetDiscriminatorOrNull(Type dataType);

    Type? GetTypeOrNull(string discriminator);
}

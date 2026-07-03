using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface INotificationDistributer
{
    Task DistributeAsync(NotificationInfo notification, Guid[]? userIds = null, Guid[]? excludedUserIds = null);
}

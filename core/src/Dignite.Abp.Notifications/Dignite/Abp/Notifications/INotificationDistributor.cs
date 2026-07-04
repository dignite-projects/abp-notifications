using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

public interface INotificationDistributor
{
    Task DistributeAsync(NotificationInfo notification, Guid[]? userIds = null, Guid[]? excludedUserIds = null);
}

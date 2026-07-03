using System;
using System.Threading.Tasks;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Decides whether a permission-gated notification is available to a specific user. The default implementation
/// grants everything; the optional Identity integration replaces it with a real ABP authorization check.
/// </summary>
public interface INotificationPermissionChecker
{
    Task<bool> IsGrantedAsync(Guid userId, string permissionName);
}

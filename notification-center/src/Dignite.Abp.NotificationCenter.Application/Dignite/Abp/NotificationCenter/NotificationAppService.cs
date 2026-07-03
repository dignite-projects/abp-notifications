using System;
using System.Linq;
using System.Threading.Tasks;
using Dignite.Abp.Notifications;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Localization;
using Volo.Abp.Users;

namespace Dignite.Abp.NotificationCenter;

[Authorize]
public class NotificationAppService : ApplicationService, INotificationAppService
{
    protected IUserNotificationManager UserNotificationManager { get; }

    protected INotificationSubscriptionManager SubscriptionManager { get; }

    protected INotificationDefinitionManager DefinitionManager { get; }

    public NotificationAppService(
        IUserNotificationManager userNotificationManager,
        INotificationSubscriptionManager subscriptionManager,
        INotificationDefinitionManager definitionManager)
    {
        UserNotificationManager = userNotificationManager;
        SubscriptionManager = subscriptionManager;
        DefinitionManager = definitionManager;
    }

    public virtual async Task<PagedResultDto<UserNotificationDto>> GetListAsync(GetUserNotificationListInput input)
    {
        var userId = CurrentUser.GetId();

        var totalCount = await UserNotificationManager.GetUserNotificationCountAsync(
            userId, input.State, input.StartDate, input.EndDate);

        var items = await UserNotificationManager.GetUserNotificationsAsync(
            userId, input.State, input.SkipCount, input.MaxResultCount, input.StartDate, input.EndDate);

        return new PagedResultDto<UserNotificationDto>(totalCount, items.Select(MapToDto).ToList());
    }

    public virtual Task<int> GetCountAsync(UserNotificationState? state = null)
    {
        return UserNotificationManager.GetUserNotificationCountAsync(CurrentUser.GetId(), state);
    }

    public virtual Task MarkAsReadAsync(Guid notificationId)
    {
        return UserNotificationManager.UpdateUserNotificationStateAsync(
            CurrentUser.GetId(), notificationId, UserNotificationState.Read);
    }

    public virtual Task MarkAllAsReadAsync()
    {
        return UserNotificationManager.UpdateAllUserNotificationStatesAsync(
            CurrentUser.GetId(), UserNotificationState.Read);
    }

    public virtual Task DeleteAsync(Guid notificationId)
    {
        return UserNotificationManager.DeleteUserNotificationAsync(CurrentUser.GetId(), notificationId);
    }

    public virtual Task DeleteAllAsync(UserNotificationState? state = null)
    {
        return UserNotificationManager.DeleteAllUserNotificationsAsync(CurrentUser.GetId(), state);
    }

    public virtual async Task<ListResultDto<NotificationSubscriptionDto>> GetSubscriptionsAsync()
    {
        var userId = CurrentUser.GetId();

        var available = await DefinitionManager.GetAllAvailableAsync(userId);
        var subscribed = await SubscriptionManager.GetSubscribedNotificationsAsync(userId);
        var subscribedNames = subscribed.Select(s => s.NotificationName).ToHashSet();

        var dtos = available.Select(definition => new NotificationSubscriptionDto
        {
            NotificationName = definition.Name,
            DisplayName = definition.DisplayName.Localize(StringLocalizerFactory).Value,
            Description = definition.Description?.Localize(StringLocalizerFactory)?.Value,
            IsSubscribed = subscribedNames.Contains(definition.Name)
        }).ToList();

        return new ListResultDto<NotificationSubscriptionDto>(dtos);
    }

    public virtual Task SubscribeAsync(string notificationName)
    {
        return SubscriptionManager.SubscribeAsync(CurrentUser.GetId(), notificationName);
    }

    public virtual Task UnsubscribeAsync(string notificationName)
    {
        return SubscriptionManager.UnsubscribeAsync(CurrentUser.GetId(), notificationName);
    }

    protected virtual UserNotificationDto MapToDto(UserNotificationWithNotification source)
    {
        // Display name is localized here, per the current reader's culture (fixes the reference implementation's
        // publish-time culture baking — roadmap problem F).
        var definition = DefinitionManager.GetOrNull(source.Notification.NotificationName);

        return new UserNotificationDto
        {
            Id = source.UserNotification.Id,
            UserId = source.UserNotification.UserId,
            NotificationId = source.Notification.Id,
            NotificationName = source.Notification.NotificationName,
            NotificationDisplayName = definition?.DisplayName.Localize(StringLocalizerFactory).Value,
            Data = source.Notification.Data,
            EntityTypeName = source.Notification.EntityTypeName,
            EntityId = source.Notification.EntityId,
            Severity = source.Notification.Severity,
            CreationTime = source.Notification.CreationTime,
            State = source.UserNotification.State
        };
    }
}

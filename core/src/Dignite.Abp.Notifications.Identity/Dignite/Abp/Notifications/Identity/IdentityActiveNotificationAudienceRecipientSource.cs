using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Dignite.Abp.Notifications.Identity;

/// <summary>
/// Pages ABP Identity users for <see cref="NotificationAudienceNames.AllActiveUsers"/>.
/// Eligible candidates are users in the requested tenant-or-host scope that are active, not leaved, and not
/// soft-deleted by ABP's normal data filters.
/// </summary>
[ExposeServices(typeof(INotificationAudienceRecipientSource))]
public class IdentityActiveNotificationAudienceRecipientSource :
    INotificationAudienceRecipientSource,
    ITransientDependency
{
    protected IRepository<IdentityUser, Guid> UserRepository { get; }

    protected IAsyncQueryableExecuter AsyncExecuter { get; }

    protected ICurrentTenant CurrentTenant { get; }

    public string AudienceName => NotificationAudienceNames.AllActiveUsers;

    public IdentityActiveNotificationAudienceRecipientSource(
        IRepository<IdentityUser, Guid> userRepository,
        IAsyncQueryableExecuter asyncExecuter,
        ICurrentTenant currentTenant)
    {
        UserRepository = userRepository;
        AsyncExecuter = asyncExecuter;
        CurrentTenant = currentTenant;
    }

    public virtual async Task<NotificationAudienceRecipientPage> GetRecipientsAsync(
        NotificationAudienceRecipientPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AudienceName != AudienceName)
        {
            throw new ArgumentException(
                $"Identity active-user recipient source cannot serve audience '{request.AudienceName}'.",
                nameof(request));
        }

        if (CurrentTenant.Id != request.TenantId)
        {
            throw new InvalidOperationException(
                $"Identity active-user recipient source must run inside tenant '{request.TenantId}', but the " +
                $"ambient tenant is '{CurrentTenant.Id}'.");
        }

        Guid? afterUserId = null;
        if (!string.IsNullOrWhiteSpace(request.ContinuationToken))
        {
            afterUserId = Guid.Parse(request.ContinuationToken);
        }

        var query = await UserRepository.GetQueryableAsync();
        query = query.Where(user =>
            user.TenantId == request.TenantId &&
            user.IsActive &&
            !user.Leaved &&
            !user.IsDeleted);

        if (afterUserId.HasValue)
        {
            var continuationUserId = afterUserId.Value;
            query = query.Where(user => user.Id.CompareTo(continuationUserId) > 0);
        }

        var takeCount = request.MaxResultCount == int.MaxValue
            ? request.MaxResultCount
            : request.MaxResultCount + 1;
        var queriedUserIds = await AsyncExecuter.ToListAsync(
            query
                .OrderBy(user => user.Id)
                .Select(user => user.Id)
                .Take(takeCount),
            cancellationToken);
        var hasMore = queriedUserIds.Count > request.MaxResultCount;
        var userIds = hasMore
            ? queriedUserIds.Take(request.MaxResultCount).ToList()
            : queriedUserIds;

        return new NotificationAudienceRecipientPage(
            userIds,
            hasMore ? userIds[^1].ToString("N") : null);
    }
}

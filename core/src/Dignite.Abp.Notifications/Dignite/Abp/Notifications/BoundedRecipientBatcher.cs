using System;
using System.Collections.Generic;
using System.Linq;

namespace Dignite.Abp.Notifications;

/// <summary>
/// Normalizes an already caller-materialized explicit recipient array without creating notification-wide
/// collections. Each pass retains at most one configured batch and advances an exclusive GUID cursor.
/// </summary>
internal static class BoundedRecipientBatcher
{
    public static bool TryNormalizeWithinLimit(
        Guid[] userIds,
        int maxResultCount,
        out Guid[] normalizedUserIds)
    {
        var seen = new HashSet<Guid>();
        var ordered = new List<Guid>(Math.Min(userIds.Length, maxResultCount));
        foreach (var userId in userIds)
        {
            if (!seen.Add(userId))
            {
                continue;
            }

            if (seen.Count > maxResultCount)
            {
                normalizedUserIds = Array.Empty<Guid>();
                return false;
            }

            ordered.Add(userId);
        }

        normalizedUserIds = ordered.ToArray();
        return true;
    }

    public static IEnumerable<Guid[]> GetDistinctBatches(
        Guid[] userIds,
        int batchSize)
    {
        if (TryNormalizeWithinLimit(userIds, batchSize, out var singleBatch))
        {
            if (singleBatch.Length > 0)
            {
                yield return singleBatch;
            }

            yield break;
        }

        Guid? afterUserId = null;
        while (true)
        {
            var nextBatch = new SortedSet<Guid>();
            foreach (var userId in userIds)
            {
                if (afterUserId.HasValue && userId.CompareTo(afterUserId.Value) <= 0)
                {
                    continue;
                }

                nextBatch.Add(userId);
                if (nextBatch.Count > batchSize)
                {
                    nextBatch.Remove(nextBatch.Max);
                }
            }

            if (nextBatch.Count == 0)
            {
                yield break;
            }

            var batch = nextBatch.ToArray();
            yield return batch;
            afterUserId = batch[^1];
        }
    }

    public static Guid[] RemoveExcludedRecipients(
        Guid[] userIds,
        Guid[]? excludedUserIds)
    {
        if (excludedUserIds is not { Length: > 0 })
        {
            return userIds;
        }

        var included = new HashSet<Guid>(userIds);
        foreach (var excludedUserId in excludedUserIds)
        {
            included.Remove(excludedUserId);
        }

        return userIds.Where(included.Contains).ToArray();
    }
}

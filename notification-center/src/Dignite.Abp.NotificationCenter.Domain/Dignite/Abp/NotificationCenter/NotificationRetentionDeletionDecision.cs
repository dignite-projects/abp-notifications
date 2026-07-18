using System;

namespace Dignite.Abp.NotificationCenter;

public class NotificationRetentionDeletionDecision
{
    public bool IsVetoed { get; }

    public string? Reason { get; }

    private NotificationRetentionDeletionDecision(bool isVetoed, string? reason)
    {
        IsVetoed = isVetoed;
        Reason = reason;
    }

    public static NotificationRetentionDeletionDecision Allow()
    {
        return new NotificationRetentionDeletionDecision(false, null);
    }

    public static NotificationRetentionDeletionDecision Veto(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A retention veto reason is required.", nameof(reason));
        }

        return new NotificationRetentionDeletionDecision(true, reason);
    }
}

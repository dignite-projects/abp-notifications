using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Dignite.Abp.NotificationCenter;

/// <summary>
/// Identifies one current-user subscription. A scope is either definition-wide (both entity fields are
/// <see langword="null"/>) or identifies one concrete entity (both entity fields are supplied).
/// </summary>
public class NotificationSubscriptionScopeDto : IValidatableObject
{
    [Required]
    [StringLength(NotificationCenterConsts.MaxNotificationNameLength)]
    public string NotificationName { get; set; } = default!;

    [StringLength(NotificationCenterConsts.MaxEntityTypeNameLength)]
    public string? EntityTypeName { get; set; }

    [StringLength(NotificationCenterConsts.MaxEntityIdLength)]
    public string? EntityId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((EntityTypeName == null) != (EntityId == null))
        {
            yield return new ValidationResult(
                "EntityTypeName and EntityId must either both be supplied or both be null.",
                new[] { nameof(EntityTypeName), nameof(EntityId) });
        }
    }
}

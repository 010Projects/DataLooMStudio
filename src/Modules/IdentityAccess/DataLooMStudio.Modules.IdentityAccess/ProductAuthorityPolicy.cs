namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityPolicy
{
    public static ProductAuthorityPolicyDecision CanUsePermission(
        string actorSubject,
        string permissionKey,
        string resourceType,
        string resourceId,
        ProductActor? actor,
        ProductPermissionAssignment? assignment,
        DateTimeOffset now)
    {
        if (!IsHumanActor(actorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied("Product authority requires a named human actor.");
        }

        if (!ProductAuthorityPermissions.IsSupported(permissionKey))
        {
            return ProductAuthorityPolicyDecision.Denied("Permission key is not part of the canonical Product authority catalog.");
        }

        if (actor is null || !actor.State.Equals(ProductActorStates.Active, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied("Actor is not active in the Product authority boundary.");
        }

        if (assignment is null)
        {
            return ProductAuthorityPolicyDecision.Denied("Actor does not have the required Product permission assignment.");
        }

        if (!assignment.State.Equals(ProductPermissionAssignmentStates.Active, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignment is not active.");
        }

        if (!assignment.ActorSubject.Equals(actorSubject, StringComparison.Ordinal)
            || !assignment.PermissionKey.Equals(permissionKey, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignment does not match the requested actor and permission.");
        }

        if (!ResourceMatches(assignment.ResourceType, resourceType)
            || !ResourceMatches(assignment.ResourceId, resourceId))
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignment does not cover the requested resource.");
        }

        if (assignment.EffectiveFrom.HasValue && assignment.EffectiveFrom.Value > now)
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignment is not yet effective.");
        }

        if (assignment.EffectiveTo.HasValue && assignment.EffectiveTo.Value <= now)
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignment has expired.");
        }

        return ProductAuthorityPolicyDecision.Allowed();
    }

    public static ProductAuthorityPolicyDecision CanSatisfySeparationOfDuty(
        string actorSubject,
        string conflictingActorSubject,
        string dutyConflict)
    {
        if (!IsHumanActor(actorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied("Product authority requires a named human actor.");
        }

        if (actorSubject.Equals(conflictingActorSubject, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied($"Separation of duty prohibits {dutyConflict}.");
        }

        return ProductAuthorityPolicyDecision.Allowed();
    }

    public static ProductAuthorityPolicyDecision CanRecordPermissionAssignment(
        string actorSubject,
        string permissionKey,
        string resourceType,
        string resourceId)
    {
        if (!IsHumanActor(actorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied("Product authority assignment requires a named human actor.");
        }

        if (!ProductAuthorityPermissions.IsSupported(permissionKey))
        {
            return ProductAuthorityPolicyDecision.Denied("Permission key is not part of the canonical Product authority catalog.");
        }

        if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId))
        {
            return ProductAuthorityPolicyDecision.Denied("Product permission assignments must name a resource scope.");
        }

        return ProductAuthorityPolicyDecision.Allowed();
    }

    public static bool IsHumanActor(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        return !subject.Equals("system", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("shared:", StringComparison.OrdinalIgnoreCase)
            && !subject.StartsWith("group:", StringComparison.OrdinalIgnoreCase)
            && !subject.Contains("@shared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ResourceMatches(string assigned, string requested)
    {
        return assigned.Equals(ProductAuthorityResourceIds.Any, StringComparison.Ordinal)
            || assigned.Equals(requested, StringComparison.Ordinal);
    }
}
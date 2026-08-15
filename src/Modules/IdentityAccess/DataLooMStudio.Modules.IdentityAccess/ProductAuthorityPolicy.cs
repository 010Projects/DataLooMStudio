namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductAuthorityPolicy
{
    public static ProductAuthorityPolicyDecision CanUsePermission(ProductAuthorityPolicyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.ActorSubject)
            || !ProductActorTypes.IsSupported(input.ActorType))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Product authority requires a valid actor.");
        }

        if (!ProductAuthorityPermissions.IsSupported(input.PermissionKey))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Permission key is not part of the canonical Product authority catalog.");
        }

        if (!string.IsNullOrWhiteSpace(input.ProductRole)
            && !ProductAuthorityRoleTaxonomy.IsSupportedRole(input.ProductRole))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product role is not part of the canonical Product authority taxonomy.");
        }

        if (input.Actor is null)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Actor is not known in the Product authority boundary.");
        }

        if (!input.Actor.Subject.Equals(input.ActorSubject, StringComparison.Ordinal)
            || !input.Actor.ActorType.Equals(input.ActorType, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Actor identity correlation is not valid for the Product authority boundary.");
        }

        if (input.Actor.ActorType.Equals(ProductActorTypes.Human, StringComparison.Ordinal)
            && !IsHumanActor(input.ActorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Human Product authority requires a named human actor.");
        }

        if (!input.Actor.State.Equals(ProductActorStates.Active, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityDisabled,
                "Actor is not active in the Product authority boundary.",
                input.Actor.AuthorityVersion);
        }

        if (input.TenantMembership is null
            || !input.TenantMembership.State.Equals(ProductMembershipStates.Active, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.TenantAccessDenied,
                "Actor does not have active tenant access in the Product authority boundary.",
                input.Actor.AuthorityVersion);
        }

        if (input.WorkspaceMembership is null
            || !input.WorkspaceMembership.State.Equals(ProductMembershipStates.Active, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.WorkspaceAccessDenied,
                "Actor does not have active workspace access in the Product authority boundary.",
                input.Actor.AuthorityVersion);
        }

        if (input.TenantMembership.AuthorityVersion != input.Actor.AuthorityVersion
            || input.WorkspaceMembership.AuthorityVersion != input.Actor.AuthorityVersion)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityStale,
                "Tenant or workspace authority state is stale.",
                input.Actor.AuthorityVersion);
        }

        if (input.CapturedAuthorityVersion.HasValue
            && input.CapturedAuthorityVersion.Value != input.Actor.AuthorityVersion)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityStale,
                "Captured authority version is stale.",
                input.Actor.AuthorityVersion);
        }

        if (input.CapturedAt.HasValue
            && input.MaximumAuthorityAge.HasValue
            && input.Now - input.CapturedAt.Value > input.MaximumAuthorityAge.Value)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityStale,
                "Captured authority context is stale.",
                input.Actor.AuthorityVersion);
        }

        if (input.RequireEntitlement && !input.HasEffectiveEntitlement)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.CapabilityNotEntitled,
                "Capability entitlement is required for the requested Product action.",
                input.Actor.AuthorityVersion);
        }

        if (IsRestrictedClassification(input.Classification)
            && !input.PermissionKey.Equals(ProductAuthorityPermissions.ReadRestrictedEvidence, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.ClassificationRestricted,
                "Evidence classification restricts the requested Product action.",
                input.Actor.AuthorityVersion);
        }

        if (IsRestrictedLifecycle(input.LifecycleState))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.LifecycleRestricted,
                "Resource lifecycle state restricts the requested Product action.",
                input.Actor.AuthorityVersion);
        }

        var assignmentDecision = TryPermitFromAssignment(input);
        if (assignmentDecision is { Succeeded: true })
        {
            return assignmentDecision;
        }

        var elevationDecision = TryPermitFromElevation(input);
        if (elevationDecision is { Succeeded: true })
        {
            return elevationDecision;
        }

        return assignmentDecision
            ?? elevationDecision
            ?? ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AssignmentRequired,
                "Actor does not have the required Product authority source.",
                input.Actor.AuthorityVersion);
    }

    public static ProductAuthorityPolicyDecision CanSatisfySeparationOfDuty(
        string actorSubject,
        string conflictingActorSubject,
        string dutyConflict)
    {
        if (!IsHumanActor(actorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Product authority requires a named human actor.");
        }

        if (actorSubject.Equals(conflictingActorSubject, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.SeparationOfDutiesViolation,
                $"Separation of duty prohibits {dutyConflict}.");
        }

        return ProductAuthorityPolicyDecision.Allowed(
            dutyConflict,
            ProductAuthoritySources.PermissionAssignment,
            1);
    }

    public static ProductAuthorityPolicyDecision CanRecordPermissionAssignment(
        string actorSubject,
        string permissionKey,
        string resourceType,
        string resourceId)
    {
        if (!IsHumanActor(actorSubject))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.IdentityInvalid,
                "Product authority assignment requires a named human actor.");
        }

        if (!ProductAuthorityPermissions.IsSupported(permissionKey))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Permission key is not part of the canonical Product authority catalog.");
        }

        if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignments must name a resource scope.");
        }

        return ProductAuthorityPolicyDecision.Allowed(
            permissionKey,
            ProductAuthoritySources.PermissionAssignment,
            1);
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
            && !subject.StartsWith("workload:", StringComparison.OrdinalIgnoreCase)
            && !subject.Contains("@shared", StringComparison.OrdinalIgnoreCase);
    }

    private static ProductAuthorityPolicyDecision? TryPermitFromAssignment(ProductAuthorityPolicyInput input)
    {
        var assignment = input.PermissionAssignment;
        if (assignment is null)
        {
            return null;
        }

        if (!assignment.State.Equals(ProductPermissionAssignmentStates.Active, StringComparison.Ordinal)
            || assignment.RevokedAt.HasValue)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignment is not active.",
                input.Actor!.AuthorityVersion);
        }

        if (!assignment.ActorSubject.Equals(input.ActorSubject, StringComparison.Ordinal)
            || !assignment.PermissionKey.Equals(input.PermissionKey, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignment does not match the requested actor and permission.",
                input.Actor!.AuthorityVersion);
        }

        if (!ResourceMatches(assignment.ResourceType, input.ResourceType)
            || !ResourceMatches(assignment.ResourceId, input.ResourceId))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignment does not cover the requested resource.",
                input.Actor!.AuthorityVersion);
        }

        if (assignment.EffectiveFrom.HasValue && assignment.EffectiveFrom.Value > input.Now)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignment is not yet effective.",
                input.Actor!.AuthorityVersion);
        }

        if (assignment.EffectiveTo.HasValue && assignment.EffectiveTo.Value <= input.Now)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product permission assignment has expired.",
                input.Actor!.AuthorityVersion);
        }

        if (assignment.AuthorityVersion != input.Actor!.AuthorityVersion)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityStale,
                "Product permission assignment authority version is stale.",
                input.Actor.AuthorityVersion);
        }

        return ProductAuthorityPolicyDecision.Allowed(
            assignment.PermissionKey,
            ProductAuthoritySources.PermissionAssignment,
            assignment.AuthorityVersion);
    }

    private static ProductAuthorityPolicyDecision? TryPermitFromElevation(ProductAuthorityPolicyInput input)
    {
        var elevation = input.Elevation;
        if (elevation is null)
        {
            return null;
        }

        if (!elevation.State.Equals(ProductAuthorityElevationStates.Active, StringComparison.Ordinal)
            || elevation.RevokedAt.HasValue)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product authority elevation is not active.",
                input.Actor!.AuthorityVersion);
        }

        if (!elevation.ActorSubject.Equals(input.ActorSubject, StringComparison.Ordinal)
            || !elevation.PermissionKey.Equals(input.PermissionKey, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product authority elevation does not match the requested actor and permission.",
                input.Actor!.AuthorityVersion);
        }

        if (!ResourceMatches(elevation.ResourceType, input.ResourceType)
            || !ResourceMatches(elevation.ResourceId, input.ResourceId))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product authority elevation does not cover the requested resource.",
                input.Actor!.AuthorityVersion);
        }

        if (elevation.EffectiveFrom > input.Now || elevation.ExpiresAt <= input.Now)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Product authority elevation is outside its approved time window.",
                input.Actor!.AuthorityVersion);
        }

        if (elevation.AuthorityVersion != input.Actor!.AuthorityVersion)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityStale,
                "Product authority elevation version is stale.",
                input.Actor.AuthorityVersion);
        }

        if (elevation.ElevationType.Equals(ProductAuthorityElevationTypes.Support, StringComparison.Ordinal)
            && !input.PermissionKey.Equals(ProductAuthorityPermissions.ReadSupportDiagnostics, StringComparison.Ordinal))
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.PermissionDenied,
                "Support elevation does not grant Evidence or approval authority.",
                input.Actor.AuthorityVersion);
        }

        if (elevation.ElevationType.Equals(ProductAuthorityElevationTypes.BreakGlass, StringComparison.Ordinal)
            && elevation.RequiresExternalStrongAuthentication
            && !input.ExternalStrongAuthenticationSatisfied)
        {
            return ProductAuthorityPolicyDecision.Denied(
                ProductAuthorityDenyReasonCodes.AuthorityUnavailable,
                "Break-glass authority requires validated external strong authentication.",
                input.Actor.AuthorityVersion);
        }

        return ProductAuthorityPolicyDecision.Allowed(
            elevation.PermissionKey,
            AuthoritySourceFor(elevation),
            elevation.AuthorityVersion);
    }

    private static bool IsRestrictedClassification(string? classification)
    {
        return classification?.Equals("Restricted", StringComparison.OrdinalIgnoreCase) == true
            || classification?.Equals("LegalHoldRestricted", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsRestrictedLifecycle(string? lifecycleState)
    {
        return lifecycleState?.Equals("Archived", StringComparison.OrdinalIgnoreCase) == true
            || lifecycleState?.Equals("Deleted", StringComparison.OrdinalIgnoreCase) == true
            || lifecycleState?.Equals("Quarantined", StringComparison.OrdinalIgnoreCase) == true
            || lifecycleState?.Equals("Superseded", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string AuthoritySourceFor(ProductAuthorityElevation elevation)
    {
        if (elevation.ElevationType.Equals(ProductAuthorityElevationTypes.Support, StringComparison.Ordinal))
        {
            return ProductAuthoritySources.SupportElevation;
        }

        if (elevation.ElevationType.Equals(ProductAuthorityElevationTypes.BreakGlass, StringComparison.Ordinal))
        {
            return ProductAuthoritySources.BreakGlassElevation;
        }

        return ProductAuthoritySources.PrivilegedElevation;
    }

    private static bool ResourceMatches(string assigned, string requested)
    {
        return assigned.Equals(ProductAuthorityResourceIds.Any, StringComparison.Ordinal)
            || assigned.Equals(requested, StringComparison.Ordinal);
    }
}
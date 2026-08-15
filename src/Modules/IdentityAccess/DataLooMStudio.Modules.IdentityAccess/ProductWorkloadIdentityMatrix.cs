namespace DataLooMStudio.Modules.IdentityAccess;

public static class ProductWorkloadIdentityMatrix
{
    private static readonly ProductWorkloadIdentityProfile[] Profiles =
    [
        new(
            "dls-web",
            "workload:dls-web",
            "Interactive API host for authenticated user requests.",
            [],
            [
                ProductAuthorityPermissions.ApplyEvidenceDecision,
                ProductAuthorityPermissions.CreateEvidenceCandidateDecision,
                ProductAuthorityPermissions.ActivateBreakGlass
            ],
            false,
            "dls-web"),
        new(
            "dls-worker",
            "workload:dls-worker",
            "Background outbox and workflow worker.",
            [ProductAuthorityPermissions.ProcessOutbox],
            [
                ProductAuthorityPermissions.ApplyEvidenceDecision,
                ProductAuthorityPermissions.CreateEvidenceCandidateDecision,
                ProductAuthorityPermissions.ManageEvidenceReviewAssignments
            ],
            false,
            "dls-worker"),
        new(
            "dls-migrate",
            "workload:dls-migrate",
            "Schema migration executor with no Product approval authority.",
            [],
            [
                ProductAuthorityPermissions.ApplyEvidenceDecision,
                ProductAuthorityPermissions.CreateEvidenceCandidateDecision,
                ProductAuthorityPermissions.ManageEvidenceReviewAssignments,
                ProductAuthorityPermissions.ReadEvidence
            ],
            false,
            "dls-migrate"),
        new(
            "scanner",
            "workload:scanner",
            "Evidence content scanner with minimum content-processing access.",
            [ProductAuthorityPermissions.ScanEvidenceContent],
            [
                ProductAuthorityPermissions.ApplyEvidenceDecision,
                ProductAuthorityPermissions.CreateEvidenceCandidateDecision,
                ProductAuthorityPermissions.ManageEvidenceReviewAssignments
            ],
            false,
            "scanner"),
        new(
            "reconciliation",
            "workload:reconciliation",
            "Reconciliation worker limited to reconciliation activity.",
            [ProductAuthorityPermissions.ReconcileOutbox],
            [
                ProductAuthorityPermissions.ApplyEvidenceDecision,
                ProductAuthorityPermissions.CreateEvidenceCandidateDecision,
                ProductAuthorityPermissions.ManageEvidenceReviewAssignments
            ],
            false,
            "reconciliation"),
        new(
            "support-tooling",
            "workload:support-tooling",
            "Support tooling boundary without unrestricted Evidence access.",
            [ProductAuthorityPermissions.ReadSupportDiagnostics],
            [
                ProductAuthorityPermissions.ReadEvidence,
                ProductAuthorityPermissions.ReadRestrictedEvidence,
                ProductAuthorityPermissions.ApplyEvidenceDecision
            ],
            false,
            "support-tooling")
    ];

    public static IReadOnlyCollection<ProductWorkloadIdentityProfile> All => Profiles;

    public static ProductWorkloadIdentityProfile? Find(string workloadName)
    {
        return Profiles.FirstOrDefault(profile => profile.WorkloadName.Equals(workloadName, StringComparison.Ordinal));
    }
}
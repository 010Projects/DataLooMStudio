namespace DataLooMStudio.Runtime.Persistence.Migrations;

public static class ModuleMigrationCatalog
{
    public static IReadOnlyList<ModuleMigrationBoundary> Boundaries { get; } =
    [
        new("IdentityAccess", "identity_access", "DataLooMStudio.Runtime.Persistence.Migrations.IdentityAccess"),
        new("WorkspaceWeave", "workspace_weave", "DataLooMStudio.Runtime.Persistence.Migrations.WorkspaceWeave"),
        new("Evidence", "evidence", "DataLooMStudio.Runtime.Persistence.Migrations.Evidence"),
        new("AuditLineage", "audit_lineage", "DataLooMStudio.Runtime.Persistence.Migrations.AuditLineage"),
        new("Retention", "retention", "DataLooMStudio.Runtime.Persistence.Migrations.Retention"),
        new("Commercial", "commercial", "DataLooMStudio.Runtime.Persistence.Migrations.Commercial"),
        new("Lifecycle", "lifecycle", "DataLooMStudio.Runtime.Persistence.Migrations.Lifecycle"),
        new("Workflows", "workflow", "DataLooMStudio.Runtime.Persistence.Migrations.Workflows"),
        new("AiGovernance", "ai_governance", "DataLooMStudio.Runtime.Persistence.Migrations.AiGovernance")
    ];
}
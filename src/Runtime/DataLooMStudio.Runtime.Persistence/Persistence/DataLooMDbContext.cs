using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Modules.AiGovernance;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Commercial;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lifecycle;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Retention;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workflows;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DataLooMStudio.Runtime.Persistence;

public sealed class DataLooMDbContext(
    DbContextOptions<DataLooMDbContext> options,
    IRequestContextAccessor? requestContextAccessor = null) : DbContext(options)
{
    private static readonly ValueConverter<TenantId, Guid> TenantIdConverter = new(
        id => id.Value,
        value => new TenantId(value));

    private static readonly ValueConverter<WorkspaceId, Guid> WorkspaceIdConverter = new(
        id => id.Value,
        value => new WorkspaceId(value));

    private static readonly ValueConverter<EvidenceId, Guid> EvidenceIdConverter = new(
        id => id.Value,
        value => new EvidenceId(value));

    private static readonly ValueConverter<EvidenceVersionId, Guid> EvidenceVersionIdConverter = new(
        id => id.Value,
        value => new EvidenceVersionId(value));

    private static readonly ValueConverter<LineageId, Guid> LineageIdConverter = new(
        id => id.Value,
        value => new LineageId(value));

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<EvidenceRecord> EvidenceRecords => Set<EvidenceRecord>();

    public DbSet<EvidenceVersion> EvidenceVersions => Set<EvidenceVersion>();

    public DbSet<EvidenceUploadAllocation> EvidenceUploadAllocations => Set<EvidenceUploadAllocation>();

    public DbSet<EvidenceContentVerification> EvidenceContentVerifications => Set<EvidenceContentVerification>();

    public DbSet<EvidenceReviewRequest> EvidenceReviewRequests => Set<EvidenceReviewRequest>();

    public DbSet<EvidenceReviewerAssignment> EvidenceReviewerAssignments => Set<EvidenceReviewerAssignment>();

    public DbSet<EvidenceCandidateDecision> EvidenceCandidateDecisions => Set<EvidenceCandidateDecision>();

    public DbSet<LineageRelationship> LineageRelationships => Set<LineageRelationship>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();

    public DbSet<LegalHold> LegalHolds => Set<LegalHold>();

    public DbSet<CapabilityEntitlement> CapabilityEntitlements => Set<CapabilityEntitlement>();

    public DbSet<LifecycleRecord> LifecycleRecords => Set<LifecycleRecord>();

    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<AiGovernancePolicy> AiGovernancePolicies => Set<AiGovernancePolicy>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    private TenantId? CurrentTenantId => requestContextAccessor?.Current?.TenantId;

    private WorkspaceId? CurrentWorkspaceId => requestContextAccessor?.Current?.WorkspaceId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTenancy(modelBuilder.Entity<Tenant>());
        ConfigureWorkspaces(modelBuilder.Entity<Workspace>());
        ConfigureEvidence(
            modelBuilder.Entity<EvidenceRecord>(),
            modelBuilder.Entity<EvidenceVersion>(),
            modelBuilder.Entity<EvidenceUploadAllocation>(),
            modelBuilder.Entity<EvidenceContentVerification>());
        ConfigureEvidenceReviewDecision(
            modelBuilder.Entity<EvidenceReviewRequest>(),
            modelBuilder.Entity<EvidenceReviewerAssignment>(),
            modelBuilder.Entity<EvidenceCandidateDecision>());
        ConfigureLineage(modelBuilder.Entity<LineageRelationship>());
        ConfigureAudit(modelBuilder.Entity<AuditEntry>());
        ConfigureRetention(modelBuilder.Entity<RetentionPolicy>(), modelBuilder.Entity<LegalHold>());
        ConfigureCommercial(modelBuilder.Entity<CapabilityEntitlement>());
        ConfigureLifecycle(modelBuilder.Entity<LifecycleRecord>());
        ConfigureWorkflows(modelBuilder.Entity<WorkflowRun>());
        ConfigureAiGovernance(modelBuilder.Entity<AiGovernancePolicy>());
        ConfigureOutbox(modelBuilder.Entity<OutboxMessage>());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureImmutableEvidenceVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureImmutableEvidenceVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureImmutableEvidenceVersions()
    {
        var immutableViolations = ChangeTracker.Entries<EvidenceVersion>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (immutableViolations.Length > 0)
        {
            throw new InvalidOperationException("Evidence versions are immutable and cannot be updated or deleted.");
        }
    }

    private void ConfigureTenancy(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", "identity_access");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).HasConversion(TenantIdConverter).ValueGeneratedNever();
        builder.Property(tenant => tenant.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.ExternalAuthority).HasMaxLength(256).IsRequired();
        builder.Property(tenant => tenant.LifecycleState).HasMaxLength(64).IsRequired();
        builder.Property(tenant => tenant.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(tenant => tenant.ConcurrencyToken).IsConcurrencyToken();
        builder.HasQueryFilter(tenant => CurrentTenantId.HasValue && tenant.Id == CurrentTenantId.Value);
        builder.HasIndex(tenant => tenant.ExternalAuthority).IsUnique();
    }

    private void ConfigureWorkspaces(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces", "workspace_weave");
        builder.HasKey(workspace => workspace.Id);
        builder.Property(workspace => workspace.Id).HasConversion(WorkspaceIdConverter).ValueGeneratedNever();
        ConfigureTenantScope(builder);
        builder.Property(workspace => workspace.Name).HasMaxLength(200).IsRequired();
        builder.Property(workspace => workspace.DataResidencyRegion).HasMaxLength(64).IsRequired();
        builder.Property(workspace => workspace.LifecycleState).HasMaxLength(64).IsRequired();
        builder.Property(workspace => workspace.CreatedBy).HasMaxLength(256).IsRequired();
        builder.Property(workspace => workspace.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(workspace => new { workspace.TenantId, workspace.Name }).IsUnique();
    }

    private void ConfigureEvidence(
        EntityTypeBuilder<EvidenceRecord> builder,
        EntityTypeBuilder<EvidenceVersion> evidenceVersion,
        EntityTypeBuilder<EvidenceUploadAllocation> uploadAllocation,
        EntityTypeBuilder<EvidenceContentVerification> contentVerification)
    {
        builder.ToTable("evidence_records", "evidence");
        builder.HasKey(evidence => evidence.Id);
        builder.Property(evidence => evidence.Id).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        builder.Property(evidence => evidence.LineageId).HasConversion(LineageIdConverter).ValueGeneratedNever();
        builder.Property(evidence => evidence.CurrentVersionId).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        builder.Property(evidence => evidence.EvidenceType).HasMaxLength(128).IsRequired();
        builder.Property(evidence => evidence.Classification).HasMaxLength(128).IsRequired();
        builder.Property(evidence => evidence.LifecycleState).HasMaxLength(64).IsRequired();
        builder.Property(evidence => evidence.RegisteredBy).HasMaxLength(256).IsRequired();
        builder.Property(evidence => evidence.VerificationStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(evidence => evidence.BlobName).HasMaxLength(1024).IsRequired();
        builder.Property(evidence => evidence.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(evidence => evidence.Sha256Hash).HasMaxLength(64).IsRequired();
        builder.Property(evidence => evidence.RetentionPolicyKey).HasMaxLength(128).IsRequired();
        builder.Property(evidence => evidence.RegistrationIdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(evidence => evidence.RegistrationRequestHash).HasMaxLength(64).IsRequired();
        builder.Property(evidence => evidence.ConcurrencyToken).IsConcurrencyToken();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(evidence => new { evidence.TenantId, evidence.WorkspaceId, evidence.LineageId }).IsUnique();
        builder.HasIndex(evidence => new { evidence.TenantId, evidence.WorkspaceId, evidence.RegistrationIdempotencyKey }).IsUnique();
        builder.HasIndex(evidence => new { evidence.TenantId, evidence.WorkspaceId, evidence.Sha256Hash });

        evidenceVersion.ToTable("evidence_versions", "evidence");
        evidenceVersion.HasKey(version => version.Id);
        evidenceVersion.Property(version => version.Id).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        evidenceVersion.Property(version => version.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        evidenceVersion.Property(version => version.OriginalFileName).HasMaxLength(512).IsRequired();
        evidenceVersion.Property(version => version.MediaType).HasMaxLength(255).IsRequired();
        evidenceVersion.Property(version => version.ContentHash).HasMaxLength(128).IsRequired();
        evidenceVersion.Property(version => version.StorageObjectReference).HasMaxLength(1024).IsRequired();
        evidenceVersion.Property(version => version.IntegrityState).HasMaxLength(64).IsRequired();
        evidenceVersion.Property(version => version.CreatedBy).HasMaxLength(256).IsRequired();
        evidenceVersion.Property(version => version.SupersedesVersionId).HasConversion(EvidenceVersionIdConverter);
        ConfigureWorkspaceScope(evidenceVersion);
        evidenceVersion.HasIndex(version => new { version.TenantId, version.WorkspaceId, version.EvidenceId, version.Sequence }).IsUnique();
        evidenceVersion.HasIndex(version => new { version.TenantId, version.WorkspaceId, version.ContentHash });

        uploadAllocation.ToTable("evidence_upload_allocations", "evidence");
        uploadAllocation.HasKey(allocation => allocation.Id);
        uploadAllocation.Property(allocation => allocation.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        uploadAllocation.Property(allocation => allocation.VersionId).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        uploadAllocation.Property(allocation => allocation.StorageObjectReference).HasMaxLength(1024).IsRequired();
        uploadAllocation.Property(allocation => allocation.UploadAuthorityHash).HasMaxLength(64).IsRequired();
        uploadAllocation.Property(allocation => allocation.PermittedOperation).HasMaxLength(32).IsRequired();
        uploadAllocation.Property(allocation => allocation.MediaType).HasMaxLength(255).IsRequired();
        uploadAllocation.Property(allocation => allocation.Status).HasMaxLength(32).IsRequired();
        uploadAllocation.Property(allocation => allocation.IdempotencyKey).HasMaxLength(128).IsRequired();
        uploadAllocation.Property(allocation => allocation.RequestHash).HasMaxLength(64).IsRequired();
        uploadAllocation.Property(allocation => allocation.CreatedBy).HasMaxLength(256).IsRequired();
        uploadAllocation.Property(allocation => allocation.ConcurrencyToken).IsConcurrencyToken();
        ConfigureWorkspaceScope(uploadAllocation);
        uploadAllocation.HasIndex(allocation => new { allocation.TenantId, allocation.WorkspaceId, allocation.EvidenceId, allocation.VersionId, allocation.IdempotencyKey }).IsUnique();
        uploadAllocation.HasIndex(allocation => new { allocation.TenantId, allocation.WorkspaceId, allocation.StorageObjectReference }).IsUnique();
        uploadAllocation.HasIndex(allocation => new { allocation.TenantId, allocation.WorkspaceId, allocation.Status, allocation.ExpiresAt });

        contentVerification.ToTable("evidence_content_verifications", "evidence");
        contentVerification.HasKey(verification => verification.Id);
        contentVerification.Property(verification => verification.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        contentVerification.Property(verification => verification.VersionId).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        contentVerification.Property(verification => verification.StorageObjectReference).HasMaxLength(1024).IsRequired();
        contentVerification.Property(verification => verification.ReceiptIdempotencyKey).HasMaxLength(128).IsRequired();
        contentVerification.Property(verification => verification.ReceiptRequestHash).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.ExpectedSha256Hash).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.ActualSha256Hash).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.IntegrityOutcome).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.ScanOutcome).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.ScannerName).HasMaxLength(128).IsRequired();
        contentVerification.Property(verification => verification.ScannerVersion).HasMaxLength(128).IsRequired();
        contentVerification.Property(verification => verification.ResultLifecycleState).HasMaxLength(64).IsRequired();
        contentVerification.Property(verification => verification.FailureReason).HasMaxLength(256);
        ConfigureWorkspaceScope(contentVerification);
        contentVerification.HasIndex(verification => new { verification.TenantId, verification.WorkspaceId, verification.AllocationId }).IsUnique();
        contentVerification.HasIndex(verification => new { verification.TenantId, verification.WorkspaceId, verification.EvidenceId, verification.VersionId, verification.ReceiptIdempotencyKey }).IsUnique();
        contentVerification.HasIndex(verification => new { verification.TenantId, verification.WorkspaceId, verification.ResultLifecycleState });
    }

    private void ConfigureLineage(EntityTypeBuilder<LineageRelationship> builder)
    {
        builder.ToTable("lineage_relationships", "audit_lineage");
        builder.HasKey(relationship => relationship.Id);
        builder.Property(relationship => relationship.SourceLineageId).HasConversion(LineageIdConverter).ValueGeneratedNever();
        builder.Property(relationship => relationship.TargetLineageId).HasConversion(LineageIdConverter).ValueGeneratedNever();
        builder.Property(relationship => relationship.RelationshipType).HasMaxLength(128).IsRequired();
        builder.Property(relationship => relationship.ActorOrProcess).HasMaxLength(256).IsRequired();
        builder.Property(relationship => relationship.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(relationship => relationship.CausationId).HasMaxLength(128).IsRequired();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(relationship => new
        {
            relationship.TenantId,
            relationship.WorkspaceId,
            relationship.SourceLineageId,
            relationship.TargetLineageId,
            relationship.RelationshipType,
            relationship.Version
        }).IsUnique();
    }

    private void ConfigureEvidenceReviewDecision(
        EntityTypeBuilder<EvidenceReviewRequest> reviewRequest,
        EntityTypeBuilder<EvidenceReviewerAssignment> reviewerAssignment,
        EntityTypeBuilder<EvidenceCandidateDecision> candidateDecision)
    {
        reviewRequest.ToTable("evidence_review_requests", "evidence");
        reviewRequest.HasKey(review => review.Id);
        reviewRequest.Property(review => review.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        reviewRequest.Property(review => review.EvidenceVersionId).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        reviewRequest.Property(review => review.LineageId).HasConversion(LineageIdConverter).ValueGeneratedNever();
        reviewRequest.Property(review => review.ReviewKind).HasMaxLength(64).IsRequired();
        reviewRequest.Property(review => review.State).HasMaxLength(64).IsRequired();
        reviewRequest.Property(review => review.RequestedBy).HasMaxLength(256).IsRequired();
        reviewRequest.Property(review => review.IdempotencyKey).HasMaxLength(128).IsRequired();
        reviewRequest.Property(review => review.RequestHash).HasMaxLength(64).IsRequired();
        reviewRequest.Property(review => review.DecidedBy).HasMaxLength(256);
        reviewRequest.Property(review => review.ConcurrencyToken).IsConcurrencyToken();
        ConfigureWorkspaceScope(reviewRequest);
        reviewRequest.HasIndex(review => new { review.TenantId, review.WorkspaceId, review.EvidenceId, review.EvidenceVersionId, review.IdempotencyKey }).IsUnique();
        reviewRequest.HasIndex(review => new { review.TenantId, review.WorkspaceId, review.State });

        reviewerAssignment.ToTable("evidence_reviewer_assignments", "evidence");
        reviewerAssignment.HasKey(assignment => assignment.Id);
        reviewerAssignment.Property(assignment => assignment.ReviewerSubject).HasMaxLength(256).IsRequired();
        reviewerAssignment.Property(assignment => assignment.Role).HasMaxLength(64).IsRequired();
        reviewerAssignment.Property(assignment => assignment.AssignedBy).HasMaxLength(256).IsRequired();
        reviewerAssignment.Property(assignment => assignment.RemovedBy).HasMaxLength(256);
        reviewerAssignment.Property(assignment => assignment.IdempotencyKey).HasMaxLength(128).IsRequired();
        reviewerAssignment.Property(assignment => assignment.RequestHash).HasMaxLength(64).IsRequired();
        reviewerAssignment.Property(assignment => assignment.ConcurrencyToken).IsConcurrencyToken();
        ConfigureWorkspaceScope(reviewerAssignment);
        reviewerAssignment.HasIndex(assignment => new { assignment.TenantId, assignment.WorkspaceId, assignment.ReviewRequestId, assignment.IdempotencyKey }).IsUnique();
        reviewerAssignment.HasIndex(assignment => new { assignment.TenantId, assignment.WorkspaceId, assignment.ReviewRequestId, assignment.ReviewerSubject, assignment.Role, assignment.IsActive });

        candidateDecision.ToTable("evidence_candidate_decisions", "evidence");
        candidateDecision.HasKey(candidate => candidate.Id);
        candidateDecision.Property(candidate => candidate.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        candidateDecision.Property(candidate => candidate.EvidenceVersionId).HasConversion(EvidenceVersionIdConverter).ValueGeneratedNever();
        candidateDecision.Property(candidate => candidate.DecisionType).HasMaxLength(64).IsRequired();
        candidateDecision.Property(candidate => candidate.State).HasMaxLength(64).IsRequired();
        candidateDecision.Property(candidate => candidate.Summary).HasMaxLength(2048).IsRequired();
        candidateDecision.Property(candidate => candidate.CreatedBy).HasMaxLength(256).IsRequired();
        candidateDecision.Property(candidate => candidate.IdempotencyKey).HasMaxLength(128).IsRequired();
        candidateDecision.Property(candidate => candidate.RequestHash).HasMaxLength(64).IsRequired();
        candidateDecision.Property(candidate => candidate.AppliedBy).HasMaxLength(256);
        candidateDecision.Property(candidate => candidate.AppliedReason).HasMaxLength(512);
        candidateDecision.Property(candidate => candidate.AppliedIdempotencyKey).HasMaxLength(128);
        candidateDecision.Property(candidate => candidate.AppliedRequestHash).HasMaxLength(64);
        candidateDecision.Property(candidate => candidate.ConcurrencyToken).IsConcurrencyToken();
        ConfigureWorkspaceScope(candidateDecision);
        candidateDecision.HasIndex(candidate => new { candidate.TenantId, candidate.WorkspaceId, candidate.ReviewRequestId, candidate.IdempotencyKey }).IsUnique();
        candidateDecision.HasIndex(candidate => new { candidate.TenantId, candidate.WorkspaceId, candidate.ReviewRequestId, candidate.State });
        candidateDecision.HasIndex(candidate => new { candidate.TenantId, candidate.WorkspaceId, candidate.SupersedesDecisionId });
    }

    private void ConfigureAudit(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries", "audit_lineage");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.ActorSubject).HasMaxLength(256).IsRequired();
        builder.Property(audit => audit.AuthorityContext).HasMaxLength(256).IsRequired();
        builder.Property(audit => audit.Action).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.TargetType).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.TargetId).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.CausationId).HasMaxLength(128).IsRequired();
        builder.Property(audit => audit.Outcome).HasMaxLength(64).IsRequired();
        builder.Property(audit => audit.MetadataJson).HasColumnType("jsonb").IsRequired();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(audit => new { audit.TenantId, audit.WorkspaceId, audit.OccurredAt });
    }

    private void ConfigureRetention(
        EntityTypeBuilder<RetentionPolicy> retentionPolicy,
        EntityTypeBuilder<LegalHold> legalHold)
    {
        retentionPolicy.ToTable("retention_policies", "retention");
        retentionPolicy.HasKey(policy => policy.Id);
        retentionPolicy.Property(policy => policy.PolicyKey).HasMaxLength(128).IsRequired();
        retentionPolicy.Property(policy => policy.Description).HasMaxLength(512);
        ConfigureWorkspaceScope(retentionPolicy);
        retentionPolicy.HasIndex(policy => new { policy.TenantId, policy.WorkspaceId, policy.PolicyKey }).IsUnique();

        legalHold.ToTable("legal_holds", "retention");
        legalHold.HasKey(hold => hold.Id);
        legalHold.Property(hold => hold.EvidenceId).HasConversion(EvidenceIdConverter).ValueGeneratedNever();
        legalHold.Property(hold => hold.Reason).HasMaxLength(512).IsRequired();
        legalHold.Property(hold => hold.PlacedBy).HasMaxLength(256).IsRequired();
        ConfigureWorkspaceScope(legalHold);
        legalHold.HasIndex(hold => new { hold.TenantId, hold.WorkspaceId, hold.EvidenceId, hold.ReleasedAt });
    }

    private void ConfigureCommercial(EntityTypeBuilder<CapabilityEntitlement> builder)
    {
        builder.ToTable("capability_entitlements", "commercial");
        builder.HasKey(entitlement => entitlement.Id);
        builder.Property(entitlement => entitlement.CapabilityKey).HasMaxLength(128).IsRequired();
        builder.Property(entitlement => entitlement.PlanKey).HasMaxLength(128).IsRequired();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(entitlement => new
        {
            entitlement.TenantId,
            entitlement.WorkspaceId,
            entitlement.CapabilityKey,
            entitlement.EffectiveFrom
        });
    }

    private void ConfigureLifecycle(EntityTypeBuilder<LifecycleRecord> builder)
    {
        builder.ToTable("lifecycle_records", "lifecycle");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.SubjectType).HasMaxLength(128).IsRequired();
        builder.Property(record => record.SubjectId).HasMaxLength(128).IsRequired();
        builder.Property(record => record.State).HasMaxLength(128).IsRequired();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(record => new
        {
            record.TenantId,
            record.WorkspaceId,
            record.SubjectType,
            record.SubjectId,
            record.Version
        }).IsUnique();
    }

    private void ConfigureWorkflows(EntityTypeBuilder<WorkflowRun> builder)
    {
        builder.ToTable("workflow_runs", "workflow");
        builder.HasKey(run => run.Id);
        builder.Property(run => run.WorkflowKey).HasMaxLength(128).IsRequired();
        builder.Property(run => run.SubjectType).HasMaxLength(128).IsRequired();
        builder.Property(run => run.SubjectId).HasMaxLength(128).IsRequired();
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32);
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(run => new { run.TenantId, run.WorkspaceId, run.WorkflowKey, run.Status });
    }

    private void ConfigureAiGovernance(EntityTypeBuilder<AiGovernancePolicy> builder)
    {
        builder.ToTable("ai_governance_policies", "ai_governance");
        builder.HasKey(policy => policy.Id);
        builder.Property(policy => policy.PolicyKey).HasMaxLength(128).IsRequired();
        builder.Property(policy => policy.ExecutionAuthority).HasMaxLength(128).IsRequired();
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(policy => new { policy.TenantId, policy.WorkspaceId, policy.PolicyKey }).IsUnique();
    }

    private void ConfigureOutbox(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", "foundation");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.OwningModule).HasMaxLength(128).IsRequired();
        builder.Property(message => message.MessageType).HasMaxLength(256).IsRequired();
        builder.Property(message => message.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(128).IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(32);
        ConfigureWorkspaceScope(builder);
        builder.HasIndex(message => new { message.TenantId, message.WorkspaceId, message.Status, message.AvailableAt });
        builder.HasIndex(message => new { message.OwningModule, message.MessageType });
    }

    private void ConfigureTenantScope<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class, ITenantScoped
    {
        builder.Property(entity => entity.TenantId).HasConversion(TenantIdConverter).ValueGeneratedNever();
        builder.HasIndex(entity => entity.TenantId);
        builder.HasQueryFilter(entity => CurrentTenantId.HasValue && entity.TenantId == CurrentTenantId.Value);
    }

    private void ConfigureWorkspaceScope<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class, IWorkspaceScoped
    {
        ConfigureTenantScope(builder);
        builder.Property(entity => entity.WorkspaceId).HasConversion(WorkspaceIdConverter).ValueGeneratedNever();
        builder.HasIndex(entity => new { entity.TenantId, entity.WorkspaceId });
        builder.HasQueryFilter(entity =>
            CurrentTenantId.HasValue
            && entity.TenantId == CurrentTenantId.Value
            && CurrentWorkspaceId.HasValue
            && entity.WorkspaceId == CurrentWorkspaceId.Value);
    }
}
using System.Text.Json;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceRegistrationService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IOutboxWriter outboxWriter,
    PostgresRlsSessionContext rlsSessionContext)
{
    public async Task<EvidenceRegistrationResult> RegisterInitialVersionAsync(
        EvidenceRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for evidence registration.");
        var now = clock.UtcNow;
        var evidenceId = EvidenceId.New();
        var versionId = EvidenceVersionId.New();
        var lineageId = LineageId.New();
        var actor = context.PrincipalSubject.ToString();
        var causationId = $"evidence-registration:{evidenceId}";

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

        dbContext.EvidenceRecords.Add(new EvidenceRecord
        {
            Id = evidenceId,
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            LineageId = lineageId,
            CurrentVersionId = versionId,
            EvidenceType = request.EvidenceType,
            Classification = request.Classification,
            LifecycleState = "Registered",
            RegisteredBy = actor,
            BlobName = request.StorageObjectReference,
            ContentType = request.MediaType,
            ContentLength = request.DeclaredSize,
            Sha256Hash = request.ContentHash,
            VerificationStatus = EvidenceVerificationStatus.Pending,
            Version = 1,
            IsImmutable = true,
            IsUnderLegalHold = false,
            RetentionPolicyKey = request.RetentionPolicyKey,
            CapturedAt = now
        });

        dbContext.EvidenceVersions.Add(new EvidenceVersion
        {
            Id = versionId,
            EvidenceId = evidenceId,
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            Sequence = 1,
            OriginalFileName = request.OriginalFileName,
            MediaType = request.MediaType,
            DeclaredSize = request.DeclaredSize,
            ContentHash = request.ContentHash,
            StorageObjectReference = request.StorageObjectReference,
            IntegrityState = "Pending",
            CreatedAt = now,
            CreatedBy = actor
        });

        dbContext.AuditEntries.Add(new AuditEntry
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            ActorSubject = actor,
            AuthorityContext = "Workspace",
            Action = "Evidence.RegisterInitialVersion",
            TargetType = "Evidence",
            TargetId = evidenceId.ToString(),
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Outcome = "Succeeded",
            MetadataJson = JsonSerializer.Serialize(new
            {
                request.EvidenceType,
                request.Classification,
                request.MediaType,
                request.DeclaredSize
            }),
            OccurredAt = now
        });

        dbContext.LineageRelationships.Add(new LineageRelationship
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            SourceLineageId = lineageId,
            TargetLineageId = lineageId,
            RelationshipType = "RegisteredInitialVersion",
            ActorOrProcess = actor,
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Version = 1,
            ValidFrom = now
        });

        await outboxWriter.AddAsync(new OutboxMessage
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            OwningModule = "Evidence",
            MessageType = "EvidenceRegistered",
            PayloadJson = JsonSerializer.Serialize(new
            {
                evidenceId = evidenceId.ToString(),
                versionId = versionId.ToString(),
                lineageId = lineageId.ToString()
            }),
            CorrelationId = context.CorrelationId,
            OccurredAt = now,
            AvailableAt = now
        }, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new EvidenceRegistrationResult(evidenceId, versionId, lineageId);
    }
}
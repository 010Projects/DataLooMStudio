using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence.IdentityAccess;
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
    IProductAuthorityService productAuthorityService,
    PostgresRlsSessionContext rlsSessionContext) : IEvidenceRegistrationService
{
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    private static readonly Regex IdempotencyRegex = new("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedEvidenceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Document",
        "Image",
        "Audio",
        "Video",
        "Archive",
        "Other"
    };

    private static readonly HashSet<string> SupportedClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        "Public",
        "Internal",
        "Confidential",
        "Restricted"
    };

    public async Task<EvidenceRegistrationResult> RegisterInitialVersionAsync(
        EvidenceRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var context = requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for evidence registration.");
        var now = clock.UtcNow;
        var evidenceId = EvidenceId.New();
        var versionId = EvidenceVersionId.New();
        var lineageId = LineageId.New();
        var actor = context.PrincipalSubject.ToString();
        if (string.IsNullOrWhiteSpace(actor) || actor.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A valid actor context is required for evidence registration.");
        }

        var idempotencyKey = NormalizeIdempotencyKey(request);
        var requestHash = ComputeRequestHash(request);
        var causationId = $"evidence-registration:{evidenceId}";
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            var workspaceIsActive = await dbContext.Workspaces.AnyAsync(
                workspace => workspace.Id == context.WorkspaceId
                    && workspace.LifecycleState == "Active",
                cancellationToken);
            if (!workspaceIsActive)
            {
                throw new EvidenceRegistrationForbiddenException("Workspace is not available within the active tenant context.");
            }

            var authority = await productAuthorityService.EvaluatePermissionAsync(
                new ProductAuthorityEvaluationRequest(
                    actor,
                    ProductAuthorityPermissions.RegisterEvidence,
                    ProductAuthorityResourceTypes.Evidence,
                    ProductAuthorityResourceIds.Any,
                    ProductCapability: ProductAuthorityCapabilities.EvidenceRegistration,
                    Action: ProductAuthorityActions.EvidenceRegister,
                    Classification: request.Classification,
                    LifecycleState: "Registered",
                    CausationId: causationId),
                cancellationToken);
            if (!authority.Succeeded)
            {
                throw new EvidenceRegistrationForbiddenException("Product authority denied Evidence registration.");
            }

            var existingRegistration = await dbContext.EvidenceRecords
                .Where(evidence => evidence.RegistrationIdempotencyKey == idempotencyKey)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingRegistration is not null)
            {
                if (!existingRegistration.RegistrationRequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceRegistrationConflictException("The idempotency key was already used for a different evidence registration request.");
                }

                var existingVersion = await dbContext.EvidenceVersions
                    .SingleAsync(version => version.Id == existingRegistration.CurrentVersionId, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new EvidenceRegistrationResult(
                    existingRegistration.Id,
                    existingRegistration.CurrentVersionId,
                    existingRegistration.LineageId,
                    existingRegistration.LifecycleState,
                    existingVersion.IntegrityState,
                    existingRegistration.CapturedAt,
                    IdempotentReplay: true);
            }

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
                RegistrationIdempotencyKey = idempotencyKey,
                RegistrationRequestHash = requestHash,
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
                    request.DeclaredSize,
                    IdempotencyKeyHash = Hash(idempotencyKey)
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
                    eventVersion = 1,
                    evidenceId = evidenceId.ToString(),
                    versionId = versionId.ToString(),
                    lineageId = lineageId.ToString(),
                    aggregateId = evidenceId.ToString(),
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString(),
                    integrityState = "Pending"
                }),
                CorrelationId = context.CorrelationId,
                OccurredAt = now,
                AvailableAt = now
            }, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new EvidenceRegistrationResult(
                evidenceId,
                versionId,
                lineageId,
                LifecycleState: "Registered",
                IntegrityState: "Pending",
                CreatedAt: now,
                IdempotentReplay: false);
        });
    }

    private static void Validate(EvidenceRegistrationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, nameof(request.EvidenceType), !SupportedEvidenceTypes.Contains(request.EvidenceType), "Unsupported evidence type.");
        AddIf(errors, nameof(request.Classification), !SupportedClassifications.Contains(request.Classification), "Unsupported classification.");
        AddIf(errors, nameof(request.OriginalFileName), string.IsNullOrWhiteSpace(request.OriginalFileName), "Original filename is required.");
        AddIf(errors, nameof(request.MediaType), !IsValidMediaType(request.MediaType), "Media type must be a valid type/subtype value.");
        AddIf(errors, nameof(request.DeclaredSize), request.DeclaredSize <= 0, "Declared size must be greater than zero.");
        AddIf(errors, nameof(request.ContentHash), !Sha256Regex.IsMatch(request.ContentHash ?? string.Empty), "Content hash must be a 64 character SHA-256 hex value.");
        AddIf(errors, nameof(request.StorageObjectReference), !IsSafeStorageReference(request.StorageObjectReference), "Storage object reference must be an internal non-public object reference.");
        AddIf(errors, nameof(request.RetentionPolicyKey), string.IsNullOrWhiteSpace(request.RetentionPolicyKey), "Retention policy key is required.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            AddIf(errors, nameof(request.IdempotencyKey), !IdempotencyRegex.IsMatch(request.IdempotencyKey), "Idempotency key must be 8-128 characters using letters, numbers, dot, underscore, colon or dash.");
        }

        if (errors.Count > 0)
        {
            throw new EvidenceRegistrationValidationException(errors);
        }
    }

    private static void AddIf(Dictionary<string, string[]> errors, string field, bool condition, string message)
    {
        if (condition)
        {
            errors[field] = [message];
        }
    }

    private static bool IsValidMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType) || mediaType.Length > 255)
        {
            return false;
        }

        var parts = mediaType.Split('/');
        return parts.Length == 2
            && parts.All(part => !string.IsNullOrWhiteSpace(part))
            && mediaType.All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));
    }

    private static bool IsSafeStorageReference(string? storageObjectReference)
    {
        if (string.IsNullOrWhiteSpace(storageObjectReference) || storageObjectReference.Length > 1024)
        {
            return false;
        }

        return !storageObjectReference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !storageObjectReference.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !storageObjectReference.Contains("sig=", StringComparison.OrdinalIgnoreCase)
            && !storageObjectReference.Contains("AccountKey", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIdempotencyKey(EvidenceRegistrationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return request.IdempotencyKey.Trim();
        }

        return $"derived:{Hash($"{request.OriginalFileName}|{request.DeclaredSize}|{request.ContentHash}|{request.StorageObjectReference}")}";
    }

    private static string ComputeRequestHash(EvidenceRegistrationRequest request)
    {
        return Hash(string.Join(
            '|',
            request.EvidenceType.Trim().ToUpperInvariant(),
            request.Classification.Trim().ToUpperInvariant(),
            request.OriginalFileName.Trim(),
            request.MediaType.Trim().ToLowerInvariant(),
            request.DeclaredSize,
            request.ContentHash.Trim().ToLowerInvariant(),
            request.StorageObjectReference.Trim(),
            request.RetentionPolicyKey.Trim()));
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
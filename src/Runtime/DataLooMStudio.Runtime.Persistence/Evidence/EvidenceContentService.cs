using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using DataLooMStudio.Infrastructure.Outbox;
using DataLooMStudio.Infrastructure.SecurityScanning;
using DataLooMStudio.Infrastructure.Storage;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.Runtime.Persistence.Security;
using DataLooMStudio.SharedKernel.Abstractions;
using DataLooMStudio.SharedKernel.Integrity;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.EntityFrameworkCore;

namespace DataLooMStudio.Runtime.Persistence.Evidence;

public sealed class EvidenceContentService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IOutboxWriter outboxWriter,
    PostgresRlsSessionContext rlsSessionContext,
    IEvidenceObjectStore objectStore,
    IEvidenceMalwareScanner malwareScanner) : IEvidenceContentService
{
    private const string Registered = "Registered";
    private const string UploadAllocated = "UploadAllocated";
    private const string Available = "Available";
    private const string Quarantined = "Quarantined";
    private const string Active = "Active";
    private const string Expired = "Expired";
    private const string Consumed = "Consumed";
    private const string Write = "Write";
    private static readonly Regex IdempotencyRegex = new("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public async Task<EvidenceUploadAllocationResult> AllocateUploadAsync(
        EvidenceUploadAllocationRequest request,
        CancellationToken cancellationToken)
    {
        ValidateUploadAllocation(request);
        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var evidence = await LoadEvidenceAsync(request.EvidenceId, cancellationToken);
            var version = await LoadCurrentVersionAsync(evidence, cancellationToken);
            var idempotencyKey = NormalizeIdempotencyKey(
                request.IdempotencyKey,
                $"derived:{Hash($"allocation|{evidence.Id}|{version.Id}")}");
            var requestHash = Hash($"allocation|{evidence.Id}|{version.Id}");

            var existingAllocation = await dbContext.EvidenceUploadAllocations
                .SingleOrDefaultAsync(allocation =>
                    allocation.EvidenceId == evidence.Id
                    && allocation.VersionId == version.Id
                    && allocation.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existingAllocation is not null)
            {
                if (!existingAllocation.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceContentConflictException("The idempotency key was already used for a different upload-allocation request.");
                }

                if (existingAllocation.ExpiresAt <= now || existingAllocation.Status == Expired)
                {
                    await ExpireAllocationAsync(evidence, existingAllocation, context, actor, now, cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    throw new EvidenceContentConflictException("Upload allocation has expired.");
                }

                var replayAuthority = await AllocateStoreAuthorityAsync(
                    context,
                    existingAllocation,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new EvidenceUploadAllocationResult(
                    evidence.Id,
                    version.Id,
                    existingAllocation.Id,
                    existingAllocation.StorageObjectReference,
                    replayAuthority.UploadAuthority,
                    existingAllocation.ExpiresAt,
                    existingAllocation.PermittedOperation,
                    existingAllocation.MaxSize,
                    existingAllocation.MediaType,
                    IdempotentReplay: true);
            }

            await ExpireStaleActiveAllocationsAsync(evidence, version, context, actor, now, cancellationToken);

            var activeAllocation = await dbContext.EvidenceUploadAllocations
                .SingleOrDefaultAsync(allocation =>
                    allocation.EvidenceId == evidence.Id
                    && allocation.VersionId == version.Id
                    && allocation.Status == Active,
                    cancellationToken);
            if (activeAllocation is not null)
            {
                throw new EvidenceContentConflictException("An active upload allocation already exists for this Evidence version.");
            }

            if (evidence.LifecycleState is not Registered and not UploadAllocated)
            {
                throw new EvidenceContentConflictException($"Evidence in lifecycle state '{evidence.LifecycleState}' cannot allocate upload authority.");
            }

            var allocationId = Guid.NewGuid();
            var expiresAt = now.AddMinutes(15);
            var storeAuthority = await objectStore.AllocateUploadAsync(
                new EvidenceUploadAuthorityRequest(
                    context.TenantId,
                    context.WorkspaceId,
                    evidence.Id,
                    version.Id,
                    allocationId,
                    expiresAt,
                    version.DeclaredSize,
                    version.MediaType),
                cancellationToken);

            var allocation = new EvidenceUploadAllocation
            {
                Id = allocationId,
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = evidence.Id,
                VersionId = version.Id,
                StorageObjectReference = storeAuthority.StorageObjectReference,
                UploadAuthorityHash = Hash(storeAuthority.UploadAuthority),
                ExpiresAt = expiresAt,
                PermittedOperation = Write,
                MaxSize = version.DeclaredSize,
                MediaType = version.MediaType,
                Status = Active,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                CreatedAt = now,
                CreatedBy = actor
            };

            evidence.LifecycleState = UploadAllocated;
            evidence.BlobName = storeAuthority.StorageObjectReference;
            evidence.ConcurrencyToken = Guid.NewGuid();

            dbContext.EvidenceUploadAllocations.Add(allocation);
            var nextLineageVersion = await GetNextLineageVersionAsync(evidence.LineageId, cancellationToken);
            AddAudit(
                context,
                actor,
                "Evidence.UploadAllocated",
                evidence.Id.ToString(),
                $"evidence-upload-allocation:{allocation.Id}",
                now,
                new
                {
                    versionId = version.Id.ToString(),
                    allocationId = allocation.Id,
                    allocation.ExpiresAt,
                    allocation.PermittedOperation,
                    allocation.MaxSize,
                    allocation.MediaType,
                    storageReferenceHash = Hash(allocation.StorageObjectReference)
                });
            AddLineage(context, actor, evidence.LineageId, "UploadAllocated", nextLineageVersion++, now, $"evidence-upload-allocation:{allocation.Id}");
            await AddOutboxAsync(
                context,
                "EvidenceUploadAllocated",
                now,
                new
                {
                    eventVersion = 1,
                    evidenceId = evidence.Id.ToString(),
                    versionId = version.Id.ToString(),
                    allocationId = allocation.Id,
                    aggregateId = evidence.Id.ToString(),
                    tenantId = context.TenantId.ToString(),
                    workspaceId = context.WorkspaceId.ToString(),
                    expiresAt = allocation.ExpiresAt
                },
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new EvidenceUploadAllocationResult(
                evidence.Id,
                version.Id,
                allocation.Id,
                allocation.StorageObjectReference,
                storeAuthority.UploadAuthority,
                allocation.ExpiresAt,
                allocation.PermittedOperation,
                allocation.MaxSize,
                allocation.MediaType,
                IdempotentReplay: false);
        });
    }

    public async Task<EvidenceContentReceiptResult> ConfirmContentReceivedAsync(
        EvidenceContentReceiptRequest request,
        CancellationToken cancellationToken)
    {
        ValidateContentReceipt(request);
        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeIdempotencyKey(
            request.IdempotencyKey,
            $"derived:{Hash($"receipt|{request.EvidenceId}|{request.VersionId}|{request.StorageObjectReference}")}");
        var requestHash = Hash($"receipt|{request.EvidenceId}|{request.VersionId}|{request.StorageObjectReference}");

        var loaded = await LoadReceiptContextAsync(request, idempotencyKey, requestHash, context, actor, now, cancellationToken);
        if (loaded.ExistingVerification is not null)
        {
            return ToReceiptResult(loaded.ExistingVerification, idempotentReplay: true);
        }

        var metadata = await objectStore.GetMetadataAsync(request.StorageObjectReference, cancellationToken);
        if (!metadata.Exists)
        {
            throw new EvidenceContentConflictException("Storage object does not exist for the active upload allocation.");
        }

        var decision = await VerifyContentAsync(
            loaded.Evidence,
            loaded.Version,
            loaded.Allocation,
            metadata,
            context,
            cancellationToken);

        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        var result = await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var evidence = await LoadEvidenceAsync(request.EvidenceId, cancellationToken);
            var version = await dbContext.EvidenceVersions
                .SingleOrDefaultAsync(item => item.Id == request.VersionId && item.EvidenceId == evidence.Id, cancellationToken)
                ?? throw new EvidenceContentForbiddenException("Evidence version is not available within the active workspace context.");
            var allocation = await dbContext.EvidenceUploadAllocations
                .SingleOrDefaultAsync(item =>
                    item.Id == loaded.Allocation.Id
                    && item.EvidenceId == evidence.Id
                    && item.VersionId == version.Id,
                    cancellationToken)
                ?? throw new EvidenceContentForbiddenException("Upload allocation is not available within the active workspace context.");
            var existingVerification = await dbContext.EvidenceContentVerifications
                .SingleOrDefaultAsync(verification => verification.AllocationId == allocation.Id, cancellationToken);
            if (existingVerification is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToReceiptResult(existingVerification, idempotentReplay: true);
            }

            if (allocation.ExpiresAt <= now || allocation.Status == Expired)
            {
                await ExpireAllocationAsync(evidence, allocation, context, actor, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new EvidenceContentConflictException("Upload allocation has expired.");
            }

            if (!allocation.StorageObjectReference.Equals(request.StorageObjectReference, StringComparison.Ordinal))
            {
                throw new EvidenceContentConflictException("Storage object reference does not match the active allocation.");
            }

            if (evidence.LifecycleState is not UploadAllocated)
            {
                throw new EvidenceContentConflictException($"Evidence in lifecycle state '{evidence.LifecycleState}' cannot confirm content receipt.");
            }

            allocation.Status = Consumed;
            allocation.ConsumedAt = now;
            allocation.ConcurrencyToken = Guid.NewGuid();
            evidence.LifecycleState = decision.LifecycleState;
            evidence.VerificationStatus = decision.LifecycleState == Available
                ? EvidenceVerificationStatus.Verified
                : EvidenceVerificationStatus.Rejected;
            evidence.BlobName = allocation.StorageObjectReference;
            evidence.ContentLength = decision.ActualSize;
            evidence.ConcurrencyToken = Guid.NewGuid();

            var verification = new EvidenceContentVerification
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = evidence.Id,
                VersionId = version.Id,
                AllocationId = allocation.Id,
                StorageObjectReference = allocation.StorageObjectReference,
                ReceiptIdempotencyKey = idempotencyKey,
                ReceiptRequestHash = requestHash,
                DeclaredSize = version.DeclaredSize,
                ActualSize = decision.ActualSize,
                ExpectedSha256Hash = version.ContentHash.ToLowerInvariant(),
                ActualSha256Hash = decision.ActualSha256Hash,
                IntegrityOutcome = decision.IntegrityOutcome,
                ScanOutcome = decision.ScanOutcome,
                ScannerName = decision.ScannerName,
                ScannerVersion = decision.ScannerVersion,
                ResultLifecycleState = decision.LifecycleState,
                FailureReason = decision.FailureReason,
                ReceivedAt = now,
                VerifiedAt = now,
                ScannedAt = decision.ScanOutcome == "NotRun" ? null : now
            };
            dbContext.EvidenceContentVerifications.Add(verification);

            var nextLineageVersion = await GetNextLineageVersionAsync(evidence.LineageId, cancellationToken);
            AddReceiptAuditAndLineage(context, actor, evidence, version, allocation, verification, now, ref nextLineageVersion);
            await AddReceiptOutboxAsync(context, evidence, version, allocation, verification, now, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToReceiptResult(verification, idempotentReplay: false);
        });

        if (result.LifecycleState == Quarantined)
        {
            await objectStore.QuarantineAsync(request.StorageObjectReference, result.FailureReason ?? "quarantined", cancellationToken);
        }

        return result;
    }

    private async Task<LoadedReceiptContext> LoadReceiptContextAsync(
        EvidenceContentReceiptRequest request,
        string idempotencyKey,
        string requestHash,
        RequestContext context,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();
        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var evidence = await LoadEvidenceAsync(request.EvidenceId, cancellationToken);
            var version = await dbContext.EvidenceVersions
                .SingleOrDefaultAsync(item => item.Id == request.VersionId && item.EvidenceId == evidence.Id, cancellationToken)
                ?? throw new EvidenceContentForbiddenException("Evidence version is not available within the active workspace context.");
            var allocation = await dbContext.EvidenceUploadAllocations
                .SingleOrDefaultAsync(item =>
                    item.EvidenceId == evidence.Id
                    && item.VersionId == version.Id
                    && item.StorageObjectReference == request.StorageObjectReference,
                    cancellationToken)
                ?? throw new EvidenceContentForbiddenException("Upload allocation is not available within the active workspace context.");
            var existingVerification = await dbContext.EvidenceContentVerifications
                .SingleOrDefaultAsync(verification => verification.AllocationId == allocation.Id, cancellationToken);

            if (existingVerification is not null)
            {
                if (!existingVerification.ReceiptRequestHash.Equals(requestHash, StringComparison.Ordinal)
                    && existingVerification.ReceiptIdempotencyKey.Equals(idempotencyKey, StringComparison.Ordinal))
                {
                    throw new EvidenceContentConflictException("The idempotency key was already used for a different content-receipt request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return new LoadedReceiptContext(evidence, version, allocation, existingVerification);
            }

            var existingByIdempotency = await dbContext.EvidenceContentVerifications
                .SingleOrDefaultAsync(verification =>
                    verification.EvidenceId == evidence.Id
                    && verification.VersionId == version.Id
                    && verification.ReceiptIdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existingByIdempotency is not null)
            {
                if (!existingByIdempotency.ReceiptRequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceContentConflictException("The idempotency key was already used for a different content-receipt request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return new LoadedReceiptContext(evidence, version, allocation, existingByIdempotency);
            }

            if (allocation.ExpiresAt <= now || allocation.Status == Expired)
            {
                await ExpireAllocationAsync(evidence, allocation, context, actor, now, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new EvidenceContentConflictException("Upload allocation has expired.");
            }

            if (allocation.Status != Active)
            {
                throw new EvidenceContentConflictException($"Upload allocation in status '{allocation.Status}' cannot confirm content receipt.");
            }

            if (evidence.LifecycleState != UploadAllocated)
            {
                throw new EvidenceContentConflictException($"Evidence in lifecycle state '{evidence.LifecycleState}' cannot confirm content receipt.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new LoadedReceiptContext(evidence, version, allocation, null);
        });
    }

    private async Task<VerificationDecision> VerifyContentAsync(
        EvidenceRecord evidence,
        EvidenceVersion version,
        EvidenceUploadAllocation allocation,
        EvidenceObjectMetadata metadata,
        RequestContext context,
        CancellationToken cancellationToken)
    {
        if (metadata.ContentLength != version.DeclaredSize)
        {
            return VerificationDecision.Quarantine(
                metadata.ContentLength,
                string.Empty,
                "SizeMismatch",
                "NotRun",
                string.Empty,
                string.Empty,
                "Actual content size does not match the declared Evidence version size.");
        }

        var actualHash = !string.IsNullOrWhiteSpace(metadata.TrustedSha256Hash)
            && Sha256Regex.IsMatch(metadata.TrustedSha256Hash)
            ? metadata.TrustedSha256Hash.ToLowerInvariant()
            : await ComputeSha256Async(allocation.StorageObjectReference, cancellationToken);

        if (!actualHash.Equals(version.ContentHash, StringComparison.OrdinalIgnoreCase))
        {
            return VerificationDecision.Quarantine(
                metadata.ContentLength,
                actualHash,
                "HashMismatch",
                "NotRun",
                string.Empty,
                string.Empty,
                "Actual content hash does not match the declared Evidence version hash.");
        }

        var scan = await malwareScanner.ScanAsync(
            new EvidenceMalwareScanRequest(
                context.TenantId,
                context.WorkspaceId,
                evidence.Id,
                version.Id,
                allocation.StorageObjectReference,
                metadata.ContentLength,
                actualHash,
                metadata.MediaType),
            cancellationToken);

        return scan.Outcome switch
        {
            EvidenceMalwareScanOutcome.Clean => VerificationDecision.Available(
                metadata.ContentLength,
                actualHash,
                scan.Outcome.ToString(),
                scan.ScannerName,
                scan.ScannerVersion),
            EvidenceMalwareScanOutcome.Malicious or EvidenceMalwareScanOutcome.Suspicious => VerificationDecision.Quarantine(
                metadata.ContentLength,
                actualHash,
                "Succeeded",
                scan.Outcome.ToString(),
                scan.ScannerName,
                scan.ScannerVersion,
                scan.Reason ?? $"Scanner returned {scan.Outcome}."),
            _ => VerificationDecision.Quarantine(
                metadata.ContentLength,
                actualHash,
                "Succeeded",
                scan.Outcome.ToString(),
                scan.ScannerName,
                scan.ScannerVersion,
                scan.Reason ?? $"Scanner returned {scan.Outcome}; content cannot become Available.")
        };
    }

    private async Task<string> ComputeSha256Async(string storageObjectReference, CancellationToken cancellationToken)
    {
        await using var stream = await objectStore.OpenReadAsync(storageObjectReference, cancellationToken);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private async Task<EvidenceUploadAuthority> AllocateStoreAuthorityAsync(
        RequestContext context,
        EvidenceUploadAllocation allocation,
        CancellationToken cancellationToken)
    {
        return await objectStore.AllocateUploadAsync(
            new EvidenceUploadAuthorityRequest(
                context.TenantId,
                context.WorkspaceId,
                allocation.EvidenceId,
                allocation.VersionId,
                allocation.Id,
                allocation.ExpiresAt,
                allocation.MaxSize,
                allocation.MediaType),
            cancellationToken);
    }

    private async Task ExpireStaleActiveAllocationsAsync(
        EvidenceRecord evidence,
        EvidenceVersion version,
        RequestContext context,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var staleAllocations = await dbContext.EvidenceUploadAllocations
            .Where(allocation =>
                allocation.EvidenceId == evidence.Id
                && allocation.VersionId == version.Id
                && allocation.Status == Active
                && allocation.ExpiresAt <= now)
            .ToArrayAsync(cancellationToken);

        foreach (var allocation in staleAllocations)
        {
            await ExpireAllocationAsync(evidence, allocation, context, actor, now, cancellationToken);
        }
    }

    private async Task ExpireAllocationAsync(
        EvidenceRecord evidence,
        EvidenceUploadAllocation allocation,
        RequestContext context,
        string actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (allocation.Status == Expired)
        {
            return;
        }

        allocation.Status = Expired;
        allocation.ConcurrencyToken = Guid.NewGuid();

        var nextLineageVersion = await GetNextLineageVersionAsync(evidence.LineageId, cancellationToken);
        AddAudit(
            context,
            actor,
            "Evidence.UploadAllocationExpired",
            evidence.Id.ToString(),
            $"evidence-upload-allocation:{allocation.Id}",
            now,
            new
            {
                allocationId = allocation.Id,
                evidenceId = evidence.Id.ToString(),
                versionId = allocation.VersionId.ToString(),
                allocation.ExpiresAt
            });
        AddLineage(context, actor, evidence.LineageId, "UploadAllocationExpired", nextLineageVersion, now, $"evidence-upload-allocation:{allocation.Id}");
        await AddOutboxAsync(
            context,
            "EvidenceUploadAllocationExpired",
            now,
            new
            {
                eventVersion = 1,
                evidenceId = evidence.Id.ToString(),
                versionId = allocation.VersionId.ToString(),
                allocationId = allocation.Id,
                aggregateId = evidence.Id.ToString(),
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString()
            },
            cancellationToken);
    }

    private void AddReceiptAuditAndLineage(
        RequestContext context,
        string actor,
        EvidenceRecord evidence,
        EvidenceVersion version,
        EvidenceUploadAllocation allocation,
        EvidenceContentVerification verification,
        DateTimeOffset now,
        ref int nextLineageVersion)
    {
        var causationId = $"evidence-content-receipt:{verification.Id}";
        AddAudit(
            context,
            actor,
            "Evidence.ContentReceived",
            evidence.Id.ToString(),
            causationId,
            now,
            new
            {
                versionId = version.Id.ToString(),
                allocationId = allocation.Id,
                verification.ActualSize,
                storageReferenceHash = Hash(verification.StorageObjectReference)
            });
        AddLineage(context, actor, evidence.LineageId, "ContentReceived", nextLineageVersion++, now, causationId);

        AddAudit(
            context,
            actor,
            "Evidence.IntegrityVerificationStarted",
            evidence.Id.ToString(),
            causationId,
            now,
            new
            {
                versionId = version.Id.ToString(),
                allocationId = allocation.Id
            });
        AddLineage(context, actor, evidence.LineageId, "IntegrityVerificationStarted", nextLineageVersion++, now, causationId);

        var integrityAction = verification.IntegrityOutcome == "Succeeded"
            ? "Evidence.IntegrityVerificationSucceeded"
            : "Evidence.IntegrityVerificationFailed";
        AddAudit(
            context,
            actor,
            integrityAction,
            evidence.Id.ToString(),
            causationId,
            now,
            new
            {
                versionId = version.Id.ToString(),
                allocationId = allocation.Id,
                verification.IntegrityOutcome,
                verification.DeclaredSize,
                verification.ActualSize
            });
        AddLineage(context, actor, evidence.LineageId, verification.IntegrityOutcome == "Succeeded" ? "IntegrityVerified" : "IntegrityFailed", nextLineageVersion++, now, causationId);

        if (verification.ScanOutcome != "NotRun")
        {
            AddAudit(
                context,
                actor,
                "Evidence.ScanRequested",
                evidence.Id.ToString(),
                causationId,
                now,
                new
                {
                    versionId = version.Id.ToString(),
                    allocationId = allocation.Id,
                    verification.ScannerName,
                    verification.ScannerVersion
                });
            AddLineage(context, actor, evidence.LineageId, "SecurityScanRequested", nextLineageVersion++, now, causationId);

            AddAudit(
                context,
                actor,
                "Evidence.ScanCompleted",
                evidence.Id.ToString(),
                causationId,
                now,
                new
                {
                    versionId = version.Id.ToString(),
                    allocationId = allocation.Id,
                    verification.ScanOutcome,
                    verification.ScannerName,
                    verification.ScannerVersion
                });
            AddLineage(context, actor, evidence.LineageId, "SecurityScanCompleted", nextLineageVersion++, now, causationId);
        }

        var finalAction = verification.ResultLifecycleState == Available
            ? "Evidence.Available"
            : "Evidence.Quarantined";
        AddAudit(
            context,
            actor,
            finalAction,
            evidence.Id.ToString(),
            causationId,
            now,
            new
            {
                versionId = version.Id.ToString(),
                allocationId = allocation.Id,
                verification.ResultLifecycleState,
                verification.FailureReason
            });
        AddLineage(context, actor, evidence.LineageId, verification.ResultLifecycleState, nextLineageVersion, now, causationId);
    }

    private async Task AddReceiptOutboxAsync(
        RequestContext context,
        EvidenceRecord evidence,
        EvidenceVersion version,
        EvidenceUploadAllocation allocation,
        EvidenceContentVerification verification,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await AddOutboxAsync(
            context,
            "EvidenceContentReceived",
            now,
            new
            {
                eventVersion = 1,
                evidenceId = evidence.Id.ToString(),
                versionId = version.Id.ToString(),
                allocationId = allocation.Id,
                verificationId = verification.Id,
                aggregateId = evidence.Id.ToString(),
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString(),
                actualSize = verification.ActualSize
            },
            cancellationToken);

        await AddOutboxAsync(
            context,
            verification.ResultLifecycleState == Available ? "EvidenceAvailable" : "EvidenceQuarantined",
            now,
            new
            {
                eventVersion = 1,
                evidenceId = evidence.Id.ToString(),
                versionId = version.Id.ToString(),
                verificationId = verification.Id,
                aggregateId = evidence.Id.ToString(),
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString(),
                lifecycleState = verification.ResultLifecycleState,
                integrityOutcome = verification.IntegrityOutcome,
                scanOutcome = verification.ScanOutcome,
                failureReason = verification.FailureReason
            },
            cancellationToken);
    }

    private async Task EnsureWorkspaceActiveAsync(RequestContext context, CancellationToken cancellationToken)
    {
        var workspaceIsActive = await dbContext.Workspaces.AnyAsync(
            workspace => workspace.Id == context.WorkspaceId
                && workspace.LifecycleState == "Active",
            cancellationToken);
        if (!workspaceIsActive)
        {
            throw new EvidenceContentForbiddenException("Workspace is not available within the active tenant context.");
        }
    }

    private async Task<EvidenceRecord> LoadEvidenceAsync(
        EvidenceId evidenceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceRecords
            .SingleOrDefaultAsync(evidence => evidence.Id == evidenceId, cancellationToken)
            ?? throw new EvidenceContentForbiddenException("Evidence is not available within the active workspace context.");
    }

    private async Task<EvidenceVersion> LoadCurrentVersionAsync(
        EvidenceRecord evidence,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceVersions
            .SingleAsync(version => version.Id == evidence.CurrentVersionId && version.EvidenceId == evidence.Id, cancellationToken);
    }

    private async Task<int> GetNextLineageVersionAsync(LineageId lineageId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.LineageRelationships
            .CountAsync(relationship =>
                relationship.SourceLineageId == lineageId
                && relationship.TargetLineageId == lineageId,
                cancellationToken);

        return existing + 1;
    }

    private void AddAudit(
        RequestContext context,
        string actor,
        string action,
        string targetId,
        string causationId,
        DateTimeOffset occurredAt,
        object metadata)
    {
        dbContext.AuditEntries.Add(new AuditEntry
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            ActorSubject = actor,
            AuthorityContext = "Workspace",
            Action = action,
            TargetType = "Evidence",
            TargetId = targetId,
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Outcome = "Succeeded",
            MetadataJson = JsonSerializer.Serialize(metadata),
            OccurredAt = occurredAt
        });
    }

    private void AddLineage(
        RequestContext context,
        string actor,
        LineageId lineageId,
        string relationshipType,
        int version,
        DateTimeOffset validFrom,
        string causationId)
    {
        dbContext.LineageRelationships.Add(new LineageRelationship
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            SourceLineageId = lineageId,
            TargetLineageId = lineageId,
            RelationshipType = relationshipType,
            ActorOrProcess = actor,
            CorrelationId = context.CorrelationId,
            CausationId = causationId,
            Version = version,
            ValidFrom = validFrom
        });
    }

    private async Task AddOutboxAsync(
        RequestContext context,
        string messageType,
        DateTimeOffset occurredAt,
        object payload,
        CancellationToken cancellationToken)
    {
        await outboxWriter.AddAsync(new OutboxMessage
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            OwningModule = "Evidence",
            MessageType = messageType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CorrelationId = context.CorrelationId,
            OccurredAt = occurredAt,
            AvailableAt = occurredAt
        }, cancellationToken);
    }

    private static void ValidateUploadAllocation(EvidenceUploadAllocationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(request.EvidenceId), request.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIdempotencyError(errors, request.IdempotencyKey);
        ThrowIfInvalid(errors);
    }

    private static void ValidateContentReceipt(EvidenceContentReceiptRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(request.EvidenceId), request.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIf(errors, nameof(request.VersionId), request.VersionId.Value == Guid.Empty, "Evidence version id is required.");
        AddIf(errors, nameof(request.StorageObjectReference), !IsSafeStorageReference(request.StorageObjectReference), "Storage object reference must match an internal allocated object reference.");
        AddIdempotencyError(errors, request.IdempotencyKey);
        ThrowIfInvalid(errors);
    }

    private static void AddIdempotencyError(Dictionary<string, string[]> errors, string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            AddIf(errors, "IdempotencyKey", !IdempotencyRegex.IsMatch(idempotencyKey), "Idempotency key must be 8-128 characters using letters, numbers, dot, underscore, colon or dash.");
        }
    }

    private static void ThrowIfInvalid(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new EvidenceContentValidationException(errors);
        }
    }

    private static void AddIf(Dictionary<string, string[]> errors, string field, bool condition, string message)
    {
        if (condition)
        {
            errors[field] = [message];
        }
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

    private static string NormalizeIdempotencyKey(string? idempotencyKey, string fallback)
    {
        return string.IsNullOrWhiteSpace(idempotencyKey) ? fallback : idempotencyKey.Trim();
    }

    private RequestContext RequireContext()
    {
        return requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence content operations.");
    }

    private static string RequireActor(RequestContext context)
    {
        var actor = context.PrincipalSubject.ToString();
        if (string.IsNullOrWhiteSpace(actor) || actor.Equals("system", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("A valid actor context is required for Evidence content operations.");
        }

        return actor;
    }

    private static EvidenceContentReceiptResult ToReceiptResult(
        EvidenceContentVerification verification,
        bool idempotentReplay)
    {
        return new EvidenceContentReceiptResult(
            verification.EvidenceId,
            verification.VersionId,
            verification.ResultLifecycleState,
            verification.IntegrityOutcome,
            verification.ScanOutcome,
            verification.FailureReason,
            verification.ActualSize,
            verification.ActualSha256Hash,
            verification.VerifiedAt,
            idempotentReplay);
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed record LoadedReceiptContext(
        EvidenceRecord Evidence,
        EvidenceVersion Version,
        EvidenceUploadAllocation Allocation,
        EvidenceContentVerification? ExistingVerification);

    private sealed record VerificationDecision(
        string LifecycleState,
        long ActualSize,
        string ActualSha256Hash,
        string IntegrityOutcome,
        string ScanOutcome,
        string ScannerName,
        string ScannerVersion,
        string? FailureReason)
    {
        public static VerificationDecision Available(
            long actualSize,
            string actualSha256Hash,
            string scanOutcome,
            string scannerName,
            string scannerVersion)
        {
            return new VerificationDecision(
                EvidenceContentService.Available,
                actualSize,
                actualSha256Hash,
                "Succeeded",
                scanOutcome,
                scannerName,
                scannerVersion,
                null);
        }

        public static VerificationDecision Quarantine(
            long actualSize,
            string actualSha256Hash,
            string integrityOutcome,
            string scanOutcome,
            string scannerName,
            string scannerVersion,
            string failureReason)
        {
            return new VerificationDecision(
                EvidenceContentService.Quarantined,
                actualSize,
                actualSha256Hash,
                integrityOutcome,
                scanOutcome,
                scannerName,
                scannerVersion,
                failureReason);
        }
    }
}
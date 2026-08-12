using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using DataLooMStudio.Infrastructure.Outbox;
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

public sealed class EvidenceReviewDecisionService(
    DataLooMDbContext dbContext,
    IRequestContextAccessor requestContextAccessor,
    IClock clock,
    IOutboxWriter outboxWriter,
    PostgresRlsSessionContext rlsSessionContext) : IEvidenceReviewDecisionService
{
    private const string Available = "Available";
    private static readonly Regex IdempotencyRegex = new("^[A-Za-z0-9._:-]{8,128}$", RegexOptions.Compiled);

    public async Task<EvidenceReviewRequestResult> RequestReviewAsync(
        EvidenceReviewRequestCommand command,
        CancellationToken cancellationToken)
    {
        ValidateReviewRequest(command);
        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeIdempotencyKey(
            command.IdempotencyKey,
            $"derived:{Hash($"review|{command.EvidenceId}|{command.EvidenceVersionId}|{command.ReviewKind}")}");
        var requestHash = Hash($"review|{command.EvidenceId}|{command.EvidenceVersionId}|{command.ReviewKind}|{command.DueAt:O}");
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var evidence = await LoadEvidenceAsync(command.EvidenceId, cancellationToken);
            if (evidence.CurrentVersionId != command.EvidenceVersionId)
            {
                throw new EvidenceReviewDecisionConflictException("Evidence review must target the current immutable Evidence version.");
            }

            if (!evidence.LifecycleState.Equals(Available, StringComparison.Ordinal))
            {
                throw new EvidenceReviewDecisionConflictException("Only available Evidence can enter review.");
            }

            var existing = await dbContext.EvidenceReviewRequests
                .SingleOrDefaultAsync(review =>
                    review.EvidenceId == command.EvidenceId
                    && review.EvidenceVersionId == command.EvidenceVersionId
                    && review.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (!existing.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceReviewDecisionConflictException("The idempotency key was already used for a different review request.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToReviewResult(existing, idempotentReplay: true);
            }

            var review = new EvidenceReviewRequest
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                EvidenceId = evidence.Id,
                EvidenceVersionId = command.EvidenceVersionId,
                LineageId = LineageId.New(),
                ReviewKind = command.ReviewKind.Trim(),
                State = EvidenceReviewStates.Requested,
                RequestedBy = actor,
                RequestedAt = now,
                DueAt = command.DueAt,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };

            dbContext.EvidenceReviewRequests.Add(review);
            AddAudit(context, actor, "EvidenceReview.Requested", "EvidenceReview", review.Id.ToString("D"), $"evidence-review:{review.Id}", now, new
            {
                evidenceId = evidence.Id.ToString(),
                evidenceVersionId = command.EvidenceVersionId.ToString(),
                review.ReviewKind
            });
            AddLineage(context, actor, evidence.LineageId, review.LineageId, "ReviewRequested", 1, now, $"evidence-review:{review.Id}");
            await AddOutboxAsync(context, "EvidenceReviewRequested", now, new
            {
                eventVersion = 1,
                aggregateId = review.Id.ToString("D"),
                reviewId = review.Id,
                evidenceId = evidence.Id.ToString(),
                evidenceVersionId = command.EvidenceVersionId.ToString(),
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString()
            }, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToReviewResult(review, idempotentReplay: false);
        });
    }

    public async Task<EvidenceReviewerAssignmentResult> AssignReviewerAsync(
        EvidenceReviewerAssignmentCommand command,
        CancellationToken cancellationToken)
    {
        ValidateReviewerAssignment(command);
        var policy = EvidenceReviewPolicy.CanAssignReviewer(command.ReviewerSubject.Trim(), command.Role.Trim());
        if (!policy.Succeeded)
        {
            throw new EvidenceReviewDecisionForbiddenException(policy.Reason!);
        }

        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeIdempotencyKey(
            command.IdempotencyKey,
            $"derived:{Hash($"assignment|{command.ReviewId}|{command.ReviewerSubject}|{command.Role}")}");
        var requestHash = Hash($"assignment|{command.ReviewId}|{command.ReviewerSubject.Trim()}|{command.Role.Trim()}");
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var review = await LoadReviewAsync(command.ReviewId, cancellationToken);
            if (EvidenceReviewStates.IsTerminal(review.State))
            {
                throw new EvidenceReviewDecisionConflictException("Cannot assign a reviewer after an authoritative decision has been applied.");
            }

            var existing = await dbContext.EvidenceReviewerAssignments
                .SingleOrDefaultAsync(assignment =>
                    assignment.ReviewRequestId == review.Id
                    && assignment.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (!existing.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceReviewDecisionConflictException("The idempotency key was already used for a different reviewer assignment.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToAssignmentResult(existing, idempotentReplay: true);
            }

            var activeDuplicate = await dbContext.EvidenceReviewerAssignments
                .AnyAsync(assignment =>
                    assignment.ReviewRequestId == review.Id
                    && assignment.ReviewerSubject == command.ReviewerSubject.Trim()
                    && assignment.Role == command.Role.Trim()
                    && assignment.IsActive,
                    cancellationToken);
            if (activeDuplicate)
            {
                throw new EvidenceReviewDecisionConflictException("Reviewer is already actively assigned with this Evidence review role.");
            }

            var assignment = new EvidenceReviewerAssignment
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                ReviewRequestId = review.Id,
                ReviewerSubject = command.ReviewerSubject.Trim(),
                Role = command.Role.Trim(),
                AssignedBy = actor,
                AssignedAt = now,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };

            dbContext.EvidenceReviewerAssignments.Add(assignment);
            review.State = EvidenceReviewStates.Assigned;
            review.Version++;
            review.ConcurrencyToken = Guid.NewGuid();

            AddAudit(context, actor, "EvidenceReview.ReviewerAssigned", "EvidenceReview", review.Id.ToString("D"), $"evidence-review-assignment:{assignment.Id}", now, new
            {
                assignmentId = assignment.Id,
                reviewerSubjectHash = Hash(assignment.ReviewerSubject),
                assignment.Role
            });
            AddLineage(context, actor, review.LineageId, review.LineageId, "ReviewerAssigned", await GetNextLineageVersionAsync(review.LineageId, cancellationToken), now, $"evidence-review-assignment:{assignment.Id}");
            await AddOutboxAsync(context, "EvidenceReviewerAssigned", now, new
            {
                eventVersion = 1,
                aggregateId = review.Id.ToString("D"),
                reviewId = review.Id,
                assignmentId = assignment.Id,
                role = assignment.Role,
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString()
            }, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToAssignmentResult(assignment, idempotentReplay: false);
        });
    }

    public async Task<EvidenceCandidateDecisionResult> CreateCandidateDecisionAsync(
        EvidenceCandidateDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCandidateDecision(command);
        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeIdempotencyKey(
            command.IdempotencyKey,
            $"derived:{Hash($"candidate|{command.ReviewId}|{command.DecisionType}|{command.Summary}|{command.SupersedesDecisionId}")}");
        var requestHash = Hash($"candidate|{command.ReviewId}|{command.DecisionType}|{command.Summary}|{command.SupersedesDecisionId}");
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var review = await LoadReviewAsync(command.ReviewId, cancellationToken);
            var assignment = await LoadActiveAssignmentAsync(review.Id, actor, cancellationToken);
            var policy = EvidenceReviewPolicy.CanCreateCandidate(actor, assignment, review);
            if (!policy.Succeeded)
            {
                throw new EvidenceReviewDecisionForbiddenException(policy.Reason!);
            }

            var existing = await dbContext.EvidenceCandidateDecisions
                .SingleOrDefaultAsync(candidate =>
                    candidate.ReviewRequestId == review.Id
                    && candidate.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (!existing.RequestHash.Equals(requestHash, StringComparison.Ordinal))
                {
                    throw new EvidenceReviewDecisionConflictException("The idempotency key was already used for a different candidate decision.");
                }

                await transaction.CommitAsync(cancellationToken);
                return ToCandidateResult(existing, idempotentReplay: true);
            }

            if (command.DecisionType == EvidenceDecisionTypes.Supersede)
            {
                if (!command.SupersedesDecisionId.HasValue)
                {
                    throw new EvidenceReviewDecisionValidationException(new Dictionary<string, string[]>
                    {
                        [nameof(command.SupersedesDecisionId)] = ["Supersede decisions must name the candidate decision being superseded."]
                    });
                }

                _ = await LoadCandidateAsync(review.Id, command.SupersedesDecisionId.Value, cancellationToken);
            }

            var candidate = new EvidenceCandidateDecision
            {
                TenantId = context.TenantId,
                WorkspaceId = context.WorkspaceId,
                ReviewRequestId = review.Id,
                EvidenceId = review.EvidenceId,
                EvidenceVersionId = review.EvidenceVersionId,
                DecisionType = command.DecisionType,
                Summary = command.Summary.Trim(),
                SupersedesDecisionId = command.SupersedesDecisionId,
                CreatedBy = actor,
                CreatedAt = now,
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash
            };

            dbContext.EvidenceCandidateDecisions.Add(candidate);
            review.State = EvidenceReviewStates.CandidateProposed;
            review.Version++;
            review.ConcurrencyToken = Guid.NewGuid();

            AddAudit(context, actor, "EvidenceReview.CandidateDecisionCreated", "EvidenceReview", review.Id.ToString("D"), $"evidence-candidate-decision:{candidate.Id}", now, new
            {
                candidateDecisionId = candidate.Id,
                candidate.DecisionType,
                candidate.SupersedesDecisionId
            });
            AddLineage(context, actor, review.LineageId, review.LineageId, "CandidateDecisionCreated", await GetNextLineageVersionAsync(review.LineageId, cancellationToken), now, $"evidence-candidate-decision:{candidate.Id}");
            await AddOutboxAsync(context, "EvidenceCandidateDecisionCreated", now, new
            {
                eventVersion = 1,
                aggregateId = review.Id.ToString("D"),
                reviewId = review.Id,
                candidateDecisionId = candidate.Id,
                decisionType = candidate.DecisionType,
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString()
            }, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToCandidateResult(candidate, idempotentReplay: false);
        });
    }

    public async Task<EvidenceAppliedDecisionResult> ApplyDecisionAsync(
        EvidenceApplyDecisionCommand command,
        CancellationToken cancellationToken)
    {
        ValidateApplyDecision(command);
        var context = RequireContext();
        var actor = RequireActor(context);
        var now = clock.UtcNow;
        var idempotencyKey = NormalizeIdempotencyKey(
            command.IdempotencyKey,
            $"derived:{Hash($"apply|{command.ReviewId}|{command.CandidateDecisionId}|{command.DecisionType}")}");
        var requestHash = Hash($"apply|{command.ReviewId}|{command.CandidateDecisionId}|{command.DecisionType}|{command.ExpectedCandidateVersion}|{command.Reason}");
        var executionStrategy = dbContext.Database.CreateExecutionStrategy();

        return await executionStrategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await rlsSessionContext.SetTransactionLocalContextAsync(cancellationToken);

            await EnsureWorkspaceActiveAsync(context, cancellationToken);
            var review = await LoadReviewAsync(command.ReviewId, cancellationToken);
            var candidate = await LoadCandidateAsync(review.Id, command.CandidateDecisionId, cancellationToken);
            if (!candidate.State.Equals(EvidenceCandidateDecisionStates.Candidate, StringComparison.Ordinal)
                && candidate.AppliedIdempotencyKey == idempotencyKey
                && candidate.AppliedRequestHash == requestHash
                && candidate.AppliedAt.HasValue)
            {
                await transaction.CommitAsync(cancellationToken);
                return ToAppliedDecisionResult(review, candidate, candidate.AppliedAt.Value, idempotentReplay: true);
            }

            var assignment = await LoadActiveAssignmentAsync(review.Id, actor, cancellationToken);
            var policy = EvidenceDecisionPolicy.CanApplyAuthoritativeDecision(
                actor,
                assignment,
                review,
                candidate,
                command.DecisionType,
                command.ExpectedCandidateVersion);
            if (!policy.Succeeded)
            {
                ThrowPolicyDenied(policy.Reason!);
            }

            if (command.DecisionType is EvidenceDecisionTypes.Reject or EvidenceDecisionTypes.RequestCorrection
                && string.IsNullOrWhiteSpace(command.Reason))
            {
                throw new EvidenceReviewDecisionValidationException(new Dictionary<string, string[]>
                {
                    [nameof(command.Reason)] = ["Reject and correction decisions require a reason."]
                });
            }

            if (command.DecisionType == EvidenceDecisionTypes.Supersede
                && candidate.SupersedesDecisionId.HasValue)
            {
                var superseded = await LoadCandidateAsync(review.Id, candidate.SupersedesDecisionId.Value, cancellationToken);
                superseded.State = EvidenceCandidateDecisionStates.Superseded;
                superseded.Version++;
                superseded.AppliedBy = actor;
                superseded.AppliedAt = now;
                superseded.AppliedReason = "Superseded by " + candidate.Id;
                superseded.ConcurrencyToken = Guid.NewGuid();
            }

            candidate.State = EvidenceDecisionTypes.ToCandidateState(command.DecisionType);
            candidate.Version++;
            candidate.AppliedBy = actor;
            candidate.AppliedAt = now;
            candidate.AppliedReason = command.Reason?.Trim();
            candidate.AppliedIdempotencyKey = idempotencyKey;
            candidate.AppliedRequestHash = requestHash;
            candidate.ConcurrencyToken = Guid.NewGuid();

            review.State = EvidenceDecisionTypes.ToReviewState(command.DecisionType);
            review.DecidedBy = actor;
            review.DecidedAt = now;
            review.Version++;
            review.ConcurrencyToken = Guid.NewGuid();

            var action = command.DecisionType switch
            {
                EvidenceDecisionTypes.Accept => "EvidenceReview.Accepted",
                EvidenceDecisionTypes.Reject => "EvidenceReview.Rejected",
                EvidenceDecisionTypes.RequestCorrection => "EvidenceReview.CorrectionRequested",
                EvidenceDecisionTypes.Supersede => "EvidenceReview.Superseded",
                _ => throw new ArgumentOutOfRangeException(nameof(command.DecisionType), command.DecisionType, "Unsupported Evidence decision type.")
            };
            AddAudit(context, actor, action, "EvidenceReview", review.Id.ToString("D"), $"evidence-decision:{candidate.Id}", now, new
            {
                candidateDecisionId = candidate.Id,
                candidate.DecisionType,
                candidate.SupersedesDecisionId,
                reasonPresent = !string.IsNullOrWhiteSpace(command.Reason)
            });
            AddLineage(context, actor, review.LineageId, review.LineageId, review.State, await GetNextLineageVersionAsync(review.LineageId, cancellationToken), now, $"evidence-decision:{candidate.Id}");
            await AddOutboxAsync(context, "EvidenceDecisionApplied", now, new
            {
                eventVersion = 1,
                aggregateId = review.Id.ToString("D"),
                reviewId = review.Id,
                candidateDecisionId = candidate.Id,
                decisionType = candidate.DecisionType,
                reviewState = review.State,
                tenantId = context.TenantId.ToString(),
                workspaceId = context.WorkspaceId.ToString()
            }, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ToAppliedDecisionResult(review, candidate, now, idempotentReplay: false);
        });
    }

    private static void ThrowPolicyDenied(string reason)
    {
        if (reason.Contains("stale", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("already", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("no longer", StringComparison.OrdinalIgnoreCase))
        {
            throw new EvidenceReviewDecisionConflictException(reason);
        }

        throw new EvidenceReviewDecisionForbiddenException(reason);
    }

    private async Task EnsureWorkspaceActiveAsync(RequestContext context, CancellationToken cancellationToken)
    {
        var workspaceIsActive = await dbContext.Workspaces.AnyAsync(
            workspace => workspace.Id == context.WorkspaceId
                && workspace.LifecycleState == "Active",
            cancellationToken);
        if (!workspaceIsActive)
        {
            throw new EvidenceReviewDecisionForbiddenException("Workspace is not available within the active tenant context.");
        }
    }

    private async Task<EvidenceRecord> LoadEvidenceAsync(
        EvidenceId evidenceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceRecords
            .SingleOrDefaultAsync(evidence => evidence.Id == evidenceId, cancellationToken)
            ?? throw new EvidenceReviewDecisionForbiddenException("Evidence is not available within the active workspace context.");
    }

    private async Task<EvidenceReviewRequest> LoadReviewAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceReviewRequests
            .SingleOrDefaultAsync(review => review.Id == reviewId, cancellationToken)
            ?? throw new EvidenceReviewDecisionForbiddenException("Evidence review is not available within the active workspace context.");
    }

    private async Task<EvidenceCandidateDecision> LoadCandidateAsync(
        Guid reviewId,
        Guid candidateDecisionId,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceCandidateDecisions
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == candidateDecisionId
                && candidate.ReviewRequestId == reviewId,
                cancellationToken)
            ?? throw new EvidenceReviewDecisionForbiddenException("Candidate decision is not available within the active workspace context.");
    }

    private async Task<EvidenceReviewerAssignment?> LoadActiveAssignmentAsync(
        Guid reviewId,
        string actor,
        CancellationToken cancellationToken)
    {
        return await dbContext.EvidenceReviewerAssignments
            .Where(assignment =>
                assignment.ReviewRequestId == reviewId
                && assignment.ReviewerSubject == actor
                && assignment.IsActive)
            .OrderByDescending(assignment => assignment.AssignedAt)
            .FirstOrDefaultAsync(cancellationToken);
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
        string targetType,
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
            AuthorityContext = "EvidenceReview",
            Action = action,
            TargetType = targetType,
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
        LineageId sourceLineageId,
        LineageId targetLineageId,
        string relationshipType,
        int version,
        DateTimeOffset validFrom,
        string causationId)
    {
        dbContext.LineageRelationships.Add(new LineageRelationship
        {
            TenantId = context.TenantId,
            WorkspaceId = context.WorkspaceId,
            SourceLineageId = sourceLineageId,
            TargetLineageId = targetLineageId,
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

    private static void ValidateReviewRequest(EvidenceReviewRequestCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(command.EvidenceId), command.EvidenceId.Value == Guid.Empty, "Evidence id is required.");
        AddIf(errors, nameof(command.EvidenceVersionId), command.EvidenceVersionId.Value == Guid.Empty, "Evidence version id is required.");
        AddIf(errors, nameof(command.ReviewKind), string.IsNullOrWhiteSpace(command.ReviewKind) || command.ReviewKind.Length > 64, "Review kind is required and must not exceed 64 characters.");
        AddIdempotencyError(errors, command.IdempotencyKey);
        ThrowIfInvalid(errors);
    }

    private static void ValidateReviewerAssignment(EvidenceReviewerAssignmentCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(command.ReviewId), command.ReviewId == Guid.Empty, "Review id is required.");
        AddIf(errors, nameof(command.ReviewerSubject), string.IsNullOrWhiteSpace(command.ReviewerSubject) || command.ReviewerSubject.Length > 256, "Reviewer subject is required and must not exceed 256 characters.");
        AddIf(errors, nameof(command.Role), string.IsNullOrWhiteSpace(command.Role) || command.Role.Length > 64, "Reviewer role is required and must not exceed 64 characters.");
        AddIdempotencyError(errors, command.IdempotencyKey);
        ThrowIfInvalid(errors);
    }

    private static void ValidateCandidateDecision(EvidenceCandidateDecisionCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(command.ReviewId), command.ReviewId == Guid.Empty, "Review id is required.");
        AddIf(errors, nameof(command.DecisionType), !EvidenceDecisionTypes.IsSupported(command.DecisionType), "Decision type is not supported.");
        AddIf(errors, nameof(command.Summary), string.IsNullOrWhiteSpace(command.Summary) || command.Summary.Length > 2048, "Decision summary is required and must not exceed 2048 characters.");
        AddIdempotencyError(errors, command.IdempotencyKey);
        ThrowIfInvalid(errors);
    }

    private static void ValidateApplyDecision(EvidenceApplyDecisionCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, nameof(command.ReviewId), command.ReviewId == Guid.Empty, "Review id is required.");
        AddIf(errors, nameof(command.CandidateDecisionId), command.CandidateDecisionId == Guid.Empty, "Candidate decision id is required.");
        AddIf(errors, nameof(command.DecisionType), !EvidenceDecisionTypes.IsSupported(command.DecisionType), "Decision type is not supported.");
        AddIf(errors, nameof(command.ExpectedCandidateVersion), command.ExpectedCandidateVersion < 1, "Expected candidate version must be positive.");
        AddIf(errors, nameof(command.Reason), command.Reason?.Length > 512, "Decision reason must not exceed 512 characters.");
        AddIdempotencyError(errors, command.IdempotencyKey);
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
            throw new EvidenceReviewDecisionValidationException(errors);
        }
    }

    private static void AddIf(Dictionary<string, string[]> errors, string field, bool condition, string message)
    {
        if (condition)
        {
            errors[field] = [message];
        }
    }

    private RequestContext RequireContext()
    {
        return requestContextAccessor.Current
            ?? throw new UnauthorizedAccessException("Tenant and workspace context is required for Evidence review decision operations.");
    }

    private static string RequireActor(RequestContext context)
    {
        var actor = context.PrincipalSubject.ToString();
        if (!EvidenceReviewPolicy.IsHumanActor(actor))
        {
            throw new UnauthorizedAccessException("A valid human actor context is required for Evidence review decision operations.");
        }

        return actor;
    }

    private static EvidenceReviewRequestResult ToReviewResult(
        EvidenceReviewRequest review,
        bool idempotentReplay)
    {
        return new EvidenceReviewRequestResult(
            review.Id,
            review.EvidenceId,
            review.EvidenceVersionId,
            review.State,
            review.Version,
            review.RequestedAt,
            idempotentReplay);
    }

    private static EvidenceReviewerAssignmentResult ToAssignmentResult(
        EvidenceReviewerAssignment assignment,
        bool idempotentReplay)
    {
        return new EvidenceReviewerAssignmentResult(
            assignment.Id,
            assignment.ReviewRequestId,
            assignment.ReviewerSubject,
            assignment.Role,
            idempotentReplay);
    }

    private static EvidenceCandidateDecisionResult ToCandidateResult(
        EvidenceCandidateDecision candidate,
        bool idempotentReplay)
    {
        return new EvidenceCandidateDecisionResult(
            candidate.Id,
            candidate.ReviewRequestId,
            candidate.DecisionType,
            candidate.State,
            candidate.Version,
            idempotentReplay);
    }

    private static EvidenceAppliedDecisionResult ToAppliedDecisionResult(
        EvidenceReviewRequest review,
        EvidenceCandidateDecision candidate,
        DateTimeOffset decidedAt,
        bool idempotentReplay)
    {
        return new EvidenceAppliedDecisionResult(
            review.Id,
            candidate.Id,
            review.State,
            candidate.State,
            candidate.Version,
            decidedAt,
            idempotentReplay);
    }

    private static string NormalizeIdempotencyKey(string? idempotencyKey, string fallback)
    {
        return string.IsNullOrWhiteSpace(idempotencyKey) ? fallback : idempotencyKey.Trim();
    }

    private static string Hash(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
using DataLooMStudio.SharedKernel.Identity;
using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.Dls.Worker.Disposal;

public sealed record EvidenceDisposalWorkItem(
    TenantId TenantId,
    WorkspaceId WorkspaceId,
    EvidenceId EvidenceId,
    Guid DisposalRecordId,
    string WorkloadIdentitySubject,
    string CorrelationId,
    string IdempotencyKey);
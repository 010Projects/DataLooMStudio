using DataLooMStudio.SharedKernel.Integrity;

namespace DataLooMStudio.SharedKernel.Lineage;

public sealed record VersionedRelationship(
    LineageId SourceLineageId,
    LineageId TargetLineageId,
    string RelationshipType,
    int Version,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);
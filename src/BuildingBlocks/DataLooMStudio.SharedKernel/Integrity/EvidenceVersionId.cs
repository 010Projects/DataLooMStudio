namespace DataLooMStudio.SharedKernel.Integrity;

public readonly record struct EvidenceVersionId(Guid Value)
{
    public static EvidenceVersionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
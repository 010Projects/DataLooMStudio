namespace DataLooMStudio.SharedKernel.Integrity;

public readonly record struct EvidenceId(Guid Value)
{
    public static EvidenceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
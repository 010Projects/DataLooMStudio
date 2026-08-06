namespace DataLooMStudio.SharedKernel.Integrity;

public readonly record struct LineageId(Guid Value)
{
    public static LineageId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
namespace DataLooMStudio.SharedKernel.Identity;

public readonly record struct WorkspaceId(Guid Value)
{
    public static WorkspaceId New() => new(Guid.NewGuid());

    public static bool TryParse(string? value, out WorkspaceId workspaceId)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            workspaceId = new WorkspaceId(parsed);
            return true;
        }

        workspaceId = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}
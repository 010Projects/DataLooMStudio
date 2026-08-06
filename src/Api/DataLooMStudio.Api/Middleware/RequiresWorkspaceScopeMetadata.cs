namespace DataLooMStudio.Api.Middleware;

public sealed class RequiresWorkspaceScopeMetadata
{
    private RequiresWorkspaceScopeMetadata()
    {
    }

    public static RequiresWorkspaceScopeMetadata Instance { get; } = new();
}
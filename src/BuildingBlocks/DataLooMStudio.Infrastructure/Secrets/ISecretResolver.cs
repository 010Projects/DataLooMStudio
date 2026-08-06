namespace DataLooMStudio.Infrastructure.Secrets;

public interface ISecretResolver
{
    Task<string> ResolveAsync(string secretName, CancellationToken cancellationToken);
}
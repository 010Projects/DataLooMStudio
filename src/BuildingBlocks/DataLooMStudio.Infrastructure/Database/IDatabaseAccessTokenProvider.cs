namespace DataLooMStudio.Infrastructure.Database;

public interface IDatabaseAccessTokenProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken);
}
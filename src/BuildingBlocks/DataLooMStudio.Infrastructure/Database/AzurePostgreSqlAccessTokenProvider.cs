using Azure.Core;

namespace DataLooMStudio.Infrastructure.Database;

public sealed class AzurePostgreSqlAccessTokenProvider(TokenCredential credential) : IDatabaseAccessTokenProvider
{
    private static readonly string[] TokenScopes =
        ["https://ossrdbms-aad.database.windows.net/.default"];

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(TokenScopes),
            cancellationToken);
        return token.Token;
    }
}
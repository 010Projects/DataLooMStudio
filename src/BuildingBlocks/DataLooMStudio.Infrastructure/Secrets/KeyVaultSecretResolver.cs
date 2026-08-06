using Azure.Core;
using Azure.Security.KeyVault.Secrets;

using DataLooMStudio.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace DataLooMStudio.Infrastructure.Secrets;

public sealed class KeyVaultSecretResolver(
    IOptionsMonitor<DataLooMInfrastructureOptions> options,
    TokenCredential credential) : ISecretResolver
{
    public async Task<string> ResolveAsync(string secretName, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.KeyVaultUri))
        {
            throw new InvalidOperationException("Key Vault URI is not configured.");
        }

        var client = new SecretClient(new Uri(current.KeyVaultUri), credential);
        var secret = await client.GetSecretAsync(secretName, cancellationToken: cancellationToken);
        return secret.Value.Value;
    }
}
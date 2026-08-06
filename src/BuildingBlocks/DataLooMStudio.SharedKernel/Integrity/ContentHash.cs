using System.Security.Cryptography;

namespace DataLooMStudio.SharedKernel.Integrity;

public readonly record struct ContentHash(string Algorithm, string Value)
{
    public static ContentHash FromSha256(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return new ContentHash("SHA-256", Convert.ToHexString(hash).ToLowerInvariant());
    }

    public static ContentHash Sha256(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new ContentHash("SHA-256", value.Trim().ToLowerInvariant());
    }

    public override string ToString() => $"{Algorithm}:{Value}";
}
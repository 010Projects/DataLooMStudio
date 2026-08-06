namespace DataLooMStudio.SharedKernel.Identity;

public readonly record struct TenantId(Guid Value)
{
    public static TenantId New() => new(Guid.NewGuid());

    public static bool TryParse(string? value, out TenantId tenantId)
    {
        if (Guid.TryParse(value, out var parsed) && parsed != Guid.Empty)
        {
            tenantId = new TenantId(parsed);
            return true;
        }

        tenantId = default;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}
namespace DataLooMStudio.SharedKernel.Identity;

public readonly record struct PrincipalSubject(string Value)
{
    public static PrincipalSubject System => new("system");

    public static PrincipalSubject FromClaim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System;
        }

        return new PrincipalSubject(value.Trim());
    }

    public override string ToString() => Value;
}
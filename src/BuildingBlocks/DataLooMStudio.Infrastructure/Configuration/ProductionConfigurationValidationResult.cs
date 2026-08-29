namespace DataLooMStudio.Infrastructure.Configuration;

public sealed record ProductionConfigurationValidationResult(IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public void ThrowIfInvalid(string componentName)
    {
        if (Succeeded)
        {
            return;
        }

        var message = string.Join("; ", Errors);
        throw new InvalidOperationException(
            $"{componentName} production configuration is invalid: {message}");
    }
}
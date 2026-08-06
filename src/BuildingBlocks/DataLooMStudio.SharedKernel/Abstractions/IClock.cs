namespace DataLooMStudio.SharedKernel.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
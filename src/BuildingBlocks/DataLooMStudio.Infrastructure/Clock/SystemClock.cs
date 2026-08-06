using DataLooMStudio.SharedKernel.Abstractions;

namespace DataLooMStudio.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
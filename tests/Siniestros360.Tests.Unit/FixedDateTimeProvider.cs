using Siniestros360.SharedKernel;

namespace Siniestros360.Tests.Unit;

internal sealed class FixedDateTimeProvider(DateTime utcNow) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = utcNow;
}

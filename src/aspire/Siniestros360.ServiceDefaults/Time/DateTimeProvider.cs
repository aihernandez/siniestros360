using Siniestros360.SharedKernel;

namespace Siniestros360.ServiceDefaults.Time;

internal sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}

namespace Siniestros360.SharedKernel;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}

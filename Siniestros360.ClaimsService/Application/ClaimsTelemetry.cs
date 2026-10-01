using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Siniestros360.ClaimsService.Application;

public static class ClaimsTelemetry
{
    public static readonly ActivitySource ActivitySource = new("Siniestros360.ClaimsService");
    private static readonly Meter Meter = new("Siniestros360.ClaimsService");
    public static readonly Counter<long> Reported = Meter.CreateCounter<long>("claims.reported.count");
    public static readonly Counter<long> Closed = Meter.CreateCounter<long>("claims.closed.count");
    public static readonly Counter<long> Cancelled = Meter.CreateCounter<long>("claims.cancelled.count");
    public static readonly Histogram<double> AssignmentDuration = Meter.CreateHistogram<double>("claims.assignment.duration", unit: "s");
}

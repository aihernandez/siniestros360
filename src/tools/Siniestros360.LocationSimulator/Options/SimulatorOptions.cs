namespace Siniestros360.LocationSimulator.Options;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    public int AdjusterCount { get; init; } = 5;
    public int UpdateIntervalMilliseconds { get; init; } = 1_000;
    public double MinSpeedKph { get; init; } = 5;
    public double MaxSpeedKph { get; init; } = 14;
    public int RandomSeed { get; init; } = 360;
    public ForwardingOptions Forwarding { get; init; } = new();
}

public sealed class ForwardingOptions
{
    public bool Enabled { get; init; }
    public string Endpoint { get; init; } =
        "http://localhost:5000/api/v1/locations/batch";
    public string? ApiKey { get; init; }
    public string LoginEndpoint { get; init; } = "http://localhost:5000/api/v1/auth/login";
    public string Email { get; init; } = "admin.demo@demo.com";
    public string Password { get; init; } = "Demo!2026";
    public int TimeoutSeconds { get; init; } = 5;
}

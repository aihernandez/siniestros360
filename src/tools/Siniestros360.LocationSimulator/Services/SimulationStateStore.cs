using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Siniestros360.LocationSimulator.Models;
using Siniestros360.LocationSimulator.Options;

namespace Siniestros360.LocationSimulator.Services;

public sealed class SimulationStateStore
{
    private readonly object _gate = new();
    private readonly SimulatorOptions _options;
    private readonly IReadOnlyList<RouteDefinition> _routes;
    private readonly TimeProvider _timeProvider;
    private Random _random;
    private List<AdjusterState> _adjusters;
    private bool _isPaused;

    public SimulationStateStore(IOptions<SimulatorOptions> options, RouteCatalog routeCatalog, TimeProvider timeProvider)
    {
        _options = options.Value;
        _routes = routeCatalog.Routes;
        _timeProvider = timeProvider;
        _random = new Random(_options.RandomSeed);
        _adjusters = CreateAdjusters();
        LastUpdatedAt = _timeProvider.GetUtcNow();
    }

    public int Count { get { lock (_gate) return _adjusters.Count; } }
    public bool IsPaused { get { lock (_gate) return _isPaused; } }
    public long GeneratedBatches { get; private set; }
    public DateTimeOffset LastUpdatedAt { get; private set; }

    public LocationBatch? Advance(TimeSpan elapsed)
    {
        lock (_gate)
        {
            if (_isPaused) return null;

            var capturedAt = _timeProvider.GetUtcNow();
            foreach (var adjuster in _adjusters)
                AdvanceAdjuster(adjuster, elapsed, capturedAt);

            GeneratedBatches++;
            LastUpdatedAt = capturedAt;
            return new LocationBatch(Guid.NewGuid(), "siniestros360-location-simulator", capturedAt,
                _adjusters.Select(ToSnapshot).ToArray());
        }
    }

    public IReadOnlyList<AdjusterLocationUpdate> GetSnapshots()
    {
        lock (_gate) return _adjusters.Select(ToSnapshot).ToArray();
    }

    public AdjusterLocationUpdate? GetSnapshot(string adjusterId)
    {
        lock (_gate)
        {
            var state = _adjusters.FirstOrDefault(x =>
                string.Equals(x.AdjusterId, adjusterId, StringComparison.OrdinalIgnoreCase));
            return state is null ? null : ToSnapshot(state);
        }
    }

    public void Pause() { lock (_gate) _isPaused = true; }
    public void Resume() { lock (_gate) _isPaused = false; }

    public void Reset()
    {
        lock (_gate)
        {
            _random = new Random(_options.RandomSeed);
            _adjusters = CreateAdjusters();
            GeneratedBatches = 0;
            LastUpdatedAt = _timeProvider.GetUtcNow();
            _isPaused = false;
        }
    }

    private List<AdjusterState> CreateAdjusters()
    {
        var result = new List<AdjusterState>(_options.AdjusterCount);
        for (var index = 0; index < _options.AdjusterCount; index++)
        {
            var route = _routes[index % _routes.Count];
            var cohort = index / _routes.Count;
            var segmentIndex = cohort % route.Points.Count;
            var start = route.Points[segmentIndex];
            var end = route.Points[(segmentIndex + 1) % route.Points.Count];
            var segmentLength = GeoMath.DistanceMeters(start, end);
            var progressFraction = ((index % 5) + 1) / 6d;

            result.Add(new AdjusterState
            {
                AdjusterId = AdjusterIdFor(index).ToString(),
                DisplayName = $"Ajustador {index + 1:00}",
                Status = index % 5 == 0 ? "EN_ROUTE" : "AVAILABLE",
                Route = route,
                SegmentIndex = segmentIndex,
                SegmentProgressMeters = segmentLength * progressFraction,
                Location = GeoMath.Interpolate(start, end, progressFraction),
                SpeedKph = NextSpeed(),
                AccuracyMeters = NextAccuracy(),
                Heading = GeoMath.BearingDegrees(start, end),
                CapturedAt = _timeProvider.GetUtcNow()
            });
        }

        return result;
    }

    private void AdvanceAdjuster(AdjusterState state, TimeSpan elapsed, DateTimeOffset capturedAt)
    {
        state.SpeedKph = Math.Clamp(state.SpeedKph + ((_random.NextDouble() - .5) * 1.5),
            _options.MinSpeedKph, _options.MaxSpeedKph);
        var movement = state.SpeedKph / 3.6 * elapsed.TotalSeconds;

        while (movement > 0)
        {
            var start = state.Route.Points[state.SegmentIndex];
            var endIndex = (state.SegmentIndex + 1) % state.Route.Points.Count;
            var end = state.Route.Points[endIndex];
            var segmentLength = GeoMath.DistanceMeters(start, end);
            var available = segmentLength - state.SegmentProgressMeters;

            if (movement < available)
            {
                state.SegmentProgressMeters += movement;
                break;
            }

            movement -= Math.Max(available, 0);
            state.SegmentIndex = endIndex;
            state.SegmentProgressMeters = 0;
        }

        var currentStart = state.Route.Points[state.SegmentIndex];
        var currentEnd = state.Route.Points[(state.SegmentIndex + 1) % state.Route.Points.Count];
        var currentLength = GeoMath.DistanceMeters(currentStart, currentEnd);
        var fraction = currentLength == 0 ? 0 : state.SegmentProgressMeters / currentLength;
        state.Location = GeoMath.Interpolate(currentStart, currentEnd, fraction);
        state.Heading = GeoMath.BearingDegrees(currentStart, currentEnd);
        state.AccuracyMeters = NextAccuracy();
        state.CapturedAt = capturedAt;
        state.Sequence++;
    }

    // Coincide con DemoAdjusters de AdjustersService: índice 0 → 1111…, 1 → 2222…, hasta 9 unidades.
    // Más allá se deriva un GUID estable; esas unidades se mueven en el mapa pero no existen en el catálogo.
    public static Guid AdjusterIdFor(int index) => index < 9
        ? Guid.Parse(new string((char)('1' + index), 32))
        : new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"siniestros360-simulated-adjuster-{index}"))[..16]);

    private double NextSpeed() => _options.MinSpeedKph + _random.NextDouble() * (_options.MaxSpeedKph - _options.MinSpeedKph);
    private double NextAccuracy() => 4 + _random.NextDouble() * 8;

    private static AdjusterLocationUpdate ToSnapshot(AdjusterState state) => new(
        state.AdjusterId, state.DisplayName, state.Status, state.Route.Name,
        Math.Round(state.Location.Latitude, 7), Math.Round(state.Location.Longitude, 7),
        Math.Round(state.AccuracyMeters, 1), Math.Round(state.SpeedKph / 3.6, 2),
        Math.Round(state.Heading, 1), state.CapturedAt, state.Sequence);

    private sealed class AdjusterState
    {
        public required string AdjusterId { get; init; }
        public required string DisplayName { get; init; }
        public required string Status { get; init; }
        public required RouteDefinition Route { get; init; }
        public int SegmentIndex { get; set; }
        public double SegmentProgressMeters { get; set; }
        public GeoPoint Location { get; set; }
        public double SpeedKph { get; set; }
        public double AccuracyMeters { get; set; }
        public double Heading { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public long Sequence { get; set; }
    }
}

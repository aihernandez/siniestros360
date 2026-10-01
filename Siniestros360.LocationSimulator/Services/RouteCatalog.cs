using Siniestros360.LocationSimulator.Models;

namespace Siniestros360.LocationSimulator.Services;

public sealed class RouteCatalog
{
    public IReadOnlyList<RouteDefinition> Routes { get; } =
    [
        new("Monterrey Centro", [new(25.66910, -100.30960), new(25.67860, -100.28350), new(25.65120, -100.28980), new(25.67710, -100.34200)]),
        new("San Pedro Garza García", [new(25.65750, -100.40220), new(25.65130, -100.37110), new(25.63660, -100.35770), new(25.66000, -100.38810)]),
        new("San Nicolás de los Garza", [new(25.75100, -100.30200), new(25.73820, -100.28200), new(25.72360, -100.31100), new(25.75410, -100.33400)]),
        new("Guadalupe", [new(25.67620, -100.25500), new(25.66810, -100.22910), new(25.65050, -100.24120), new(25.69720, -100.25630)]),
        new("Apodaca", [new(25.78140, -100.18820), new(25.77610, -100.13620), new(25.80720, -100.09700), new(25.75880, -100.20300)]),
        new("Santa Catarina", [new(25.67520, -100.45810), new(25.68140, -100.50100), new(25.64840, -100.44330), new(25.70400, -100.43610)])
    ];
}

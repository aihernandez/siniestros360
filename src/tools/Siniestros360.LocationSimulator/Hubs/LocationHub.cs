using Microsoft.AspNetCore.SignalR;
using Siniestros360.LocationSimulator.Services;

namespace Siniestros360.LocationSimulator.Hubs;

public sealed class LocationHub(SimulationStateStore store) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("adjusterLocationsUpdated", store.GetSnapshots(), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

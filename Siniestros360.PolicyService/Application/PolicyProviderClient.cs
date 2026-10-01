using System.Net.Http.Json;
namespace Siniestros360.PolicyService.Application;

public interface IPolicyProviderClient { Task<PolicyProviderResult> ValidateAsync(string policyNumber, CancellationToken ct); }
public sealed record PolicyProviderResult(string CoverageStatus, string? Reason);
public sealed class PolicyProviderClient(HttpClient client, IConfiguration configuration) : IPolicyProviderClient
{
    public async Task<PolicyProviderResult> ValidateAsync(string policyNumber, CancellationToken ct)
    {
        var endpoint = configuration["PolicyProvider:Endpoint"];
        // PROVISIONAL(2026-09-30): sin PolicyProvider:Endpoint se simula el proveedor (pólizas que terminan en 0 = no vigentes) — se cierra al conectar la base real de pólizas de la aseguradora.
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            await Task.Delay(configuration.GetValue("PolicyProvider:SimulationDelayMs", 150), ct);
            return policyNumber.EndsWith("0", StringComparison.Ordinal) ? new("Rejected", "Demo policy is not active.") : new("Active", null);
        }
        return await client.GetFromJsonAsync<PolicyProviderResult>($"{endpoint.TrimEnd('/')}/policies/{Uri.EscapeDataString(policyNumber)}/coverage", ct) ?? throw new HttpRequestException("Policy provider returned an empty response.");
    }
}

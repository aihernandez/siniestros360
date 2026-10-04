using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Siniestros360.LocationSimulator.Hubs;
using Siniestros360.LocationSimulator.Models;
using Siniestros360.LocationSimulator.Options;

namespace Siniestros360.LocationSimulator.Publishing;

public sealed class LocationPublisher(
    IHubContext<LocationHub> hub,
    IHttpClientFactory httpClientFactory,
    IOptions<SimulatorOptions> options,
    ILogger<LocationPublisher> logger) : ILocationPublisher
{
    public const string HttpClientName = "location-forwarder";
    private readonly SimulatorOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt;

    public async Task PublishAsync(LocationBatch batch, CancellationToken cancellationToken)
    {
        await hub.Clients.All.SendAsync("adjusterLocationsUpdated", batch, cancellationToken);

        if (!_options.Forwarding.Enabled) return;

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(_options.Forwarding.TimeoutSeconds);
            var token = await GetAccessToken(client, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Forwarding.Endpoint)
            {
                Content = JsonContent.Create(new
                {
                    locations = batch.Locations.Select(location => new
                    {
                        adjusterId = Guid.Parse(location.AdjusterId),
                        location.Latitude,
                        location.Longitude,
                        speedKmh = location.SpeedMps * 3.6,
                        location.Heading,
                        location.Sequence,
                        location.CapturedAt
                    })
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", batch.BatchId.ToString());

            if (!string.IsNullOrWhiteSpace(_options.Forwarding.ApiKey))
                request.Headers.TryAddWithoutValidation("X-API-Key", _options.Forwarding.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("El backend rechazó el lote {BatchId} con HTTP {StatusCode}", batch.BatchId, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Terminó el tiempo de espera al reenviar el lote {BatchId}", batch.BatchId);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "No fue posible reenviar el lote {BatchId}", batch.BatchId);
        }
    }

    private async Task<string> GetAccessToken(HttpClient client, CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1)) return _accessToken;
            using var response = await client.PostAsJsonAsync(_options.Forwarding.LoginEndpoint, new { _options.Forwarding.Email, _options.Forwarding.Password }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Identity service returned an empty login response.");
            _accessToken = token.AccessToken;
            _tokenExpiresAt = token.ExpiresAt;
            return _accessToken;
        }
        finally { _tokenLock.Release(); }
    }


    private sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt);
}

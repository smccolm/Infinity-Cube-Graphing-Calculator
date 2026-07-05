using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using GraphCalc.Shared;

namespace GraphCalc.UI;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<HealthResponse> HealthAsync(CancellationToken cancellationToken = default)
    {
        HealthResponse? response = await _http.GetFromJsonAsync<HealthResponse>("/health", _jsonOptions, cancellationToken);
        return response ?? throw new InvalidOperationException("The API health response was empty.");
    }

    public async Task SetTestModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync("/test-mode", new TestModeRequest { Enabled = enabled }, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<GraphFunctionDto> UpsertFunctionAsync(UpsertFunctionRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync("/functions", request, _jsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        GraphFunctionDto? dto = await response.Content.ReadFromJsonAsync<GraphFunctionDto>(_jsonOptions, cancellationToken);
        return dto ?? throw new InvalidOperationException("The API function response was empty.");
    }

    public async Task<CalculationResultDto> CalculateAsync(CalculationRequest request, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync("/calculate", request, _jsonOptions, cancellationToken);
        CalculationResultDto? dto = await response.Content.ReadFromJsonAsync<CalculationResultDto>(_jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(dto?.ErrorMessage ?? $"Calculation failed with HTTP {(int)response.StatusCode}.");
        }
        return dto ?? throw new InvalidOperationException("The API calculation response was empty.");
    }
}

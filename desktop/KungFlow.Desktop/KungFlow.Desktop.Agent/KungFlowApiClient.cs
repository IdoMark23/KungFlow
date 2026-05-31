using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KungFlow.Desktop.Agent;

public sealed class KungFlowApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient httpClient;

    public KungFlowApiClient()
        : this(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:3000") })
    {
    }

    private KungFlowApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<LoginResponse> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password, "desktop"),
            JsonOptions,
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ApiError? apiError = Deserialize<ApiError>(body);
            throw new InvalidOperationException(apiError?.Error ?? "KungFlow server request failed.");
        }

        LoginResponse? loginResponse = Deserialize<LoginResponse>(body);
        return loginResponse ?? throw new InvalidOperationException("KungFlow server returned an empty login response.");
    }

    public async Task<LoginResponse> RegisterAndLoginAsync(
        string email,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage registerResponse = await httpClient.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(email, username, password),
            JsonOptions,
            cancellationToken);

        string registerBody = await registerResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!registerResponse.IsSuccessStatusCode)
        {
            ApiError? apiError = Deserialize<ApiError>(registerBody);
            throw new InvalidOperationException(apiError?.Error ?? "KungFlow server request failed.");
        }

        return await LoginAsync(email, password, cancellationToken);
    }

    public async Task<CurrentStatusResponse> GetCurrentStatusAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/api/status/current");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ApiError? apiError = Deserialize<ApiError>(body);
            throw new InvalidOperationException(apiError?.Error ?? "KungFlow server request failed.");
        }

        CurrentStatusResponse? currentStatus = Deserialize<CurrentStatusResponse>(body);
        return currentStatus ?? throw new InvalidOperationException("KungFlow server returned an empty status response.");
    }

    public async Task<MetricsResponse> SendMetricsAsync(
        string accessToken,
        DesktopMetricsSnapshot metrics,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(
            new MetricsRequest(
                metrics.Timestamp,
                "desktop",
                new MetricsPayload(
                    metrics.OpenWindowsCount,
                    metrics.WindowSwitchCount,
                    metrics.DeleteKeyCount,
                    metrics.KeyPressCount,
                    metrics.TypingSpeed,
                    metrics.MouseSpeed)),
            options: JsonOptions);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ApiError? apiError = Deserialize<ApiError>(body);
            throw new InvalidOperationException(apiError?.Error ?? "KungFlow server request failed.");
        }

        MetricsResponse? metricsResponse = Deserialize<MetricsResponse>(body);
        return metricsResponse ?? throw new InvalidOperationException("KungFlow server returned an empty metrics response.");
    }

    public async Task LogoutAsync(string accessToken, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            ApiError? apiError = Deserialize<ApiError>(body);
            throw new InvalidOperationException(apiError?.Error ?? "KungFlow server request failed.");
        }
    }

    private static T? Deserialize<T>(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }
}

internal sealed record LoginRequest(string Email, string Password, string Platform);

internal sealed record RegisterRequest(string Email, string Username, string Password);

internal sealed record MetricsRequest(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("metrics")] MetricsPayload Metrics);

internal sealed record MetricsPayload(
    [property: JsonPropertyName("openWindowsCount")] int OpenWindowsCount,
    [property: JsonPropertyName("windowSwitchCount")] int WindowSwitchCount,
    [property: JsonPropertyName("deleteKeyCount")] int DeleteKeyCount,
    [property: JsonPropertyName("keyPressCount")] int KeyPressCount,
    [property: JsonPropertyName("typingSpeed")] double TypingSpeed,
    [property: JsonPropertyName("mouseSpeed")] double MouseSpeed);

public sealed record LoginResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("user")] LoginUser User);

public sealed record LoginUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("username")] string Username);

public sealed record CurrentStatusResponse(
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("cognitiveLoadScore")] double? CognitiveLoadScore,
    [property: JsonPropertyName("baselineScore")] double? BaselineScore,
    [property: JsonPropertyName("comparisonBaselineScore")] double? ComparisonBaselineScore,
    [property: JsonPropertyName("baselineSamplesCollected")] int? BaselineSamplesCollected,
    [property: JsonPropertyName("baselineSamplesRequired")] int? BaselineSamplesRequired,
    [property: JsonPropertyName("shouldSilenceNotifications")] bool ShouldSilenceNotifications,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt);

public sealed record MetricsResponse(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("ignored")] bool? Ignored,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("status")] CurrentStatusResponse? Status);

internal sealed record ApiError([property: JsonPropertyName("error")] string Error);

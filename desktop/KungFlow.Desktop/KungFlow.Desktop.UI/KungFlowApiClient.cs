using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KungFlow.Desktop.UI;

internal sealed class KungFlowApiClient
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

internal sealed record LoginResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("user")] LoginUser User);

internal sealed record LoginUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("username")] string Username);

internal sealed record ApiError([property: JsonPropertyName("error")] string Error);

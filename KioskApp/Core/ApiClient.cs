using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace KioskApp.Core;

// Thrown when the API answers, but with an error (400, 404, 409, 500...).
// If the API cannot be reached at all, .NET throws HttpRequestException instead.
public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

// Every call to TrackerApi lives here (like api.js in the web version).
public sealed class ApiClient : IDisposable
{
    // "Web" defaults = camelCase names, matching the API's JSON.
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private readonly HttpClient _http;

    // kioskKey: the shared secret from kiosk.settings.json; the API refuses the kiosk without it.
    // computerId: read on every request, so the audit log shows which PC did what.
    public ApiClient(string baseUrl, string kioskKey, Func<int> computerId)
    {
        var handler = new KioskHeaders(kioskKey, computerId) { InnerHandler = new HttpClientHandler() };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public Task<List<ComputerDto>> GetComputersAsync(CancellationToken ct = default) =>
        GetAsync<List<ComputerDto>>("api/computers", ct);

    public Task<ComputerDto> GetComputerAsync(int id, CancellationToken ct = default) =>
        GetAsync<ComputerDto>($"api/computers/{id}", ct);

    public async Task SendHeartbeatAsync(int computerId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"api/computers/{computerId}/heartbeat", null, ct);
        await EnsureSuccessAsync(response, ct);
    }

    public Task<List<SessionDto>> GetActiveSessionsAsync(CancellationToken ct = default) =>
        GetAsync<List<SessionDto>>("api/sessions/active", ct);

    public Task<SessionDto> StartSessionAsync(int computerId, string userName, string purpose, CancellationToken ct = default) =>
        PostAsync<SessionDto>("api/sessions/start", new { computerId, userName, purpose }, ct);

    public Task<SessionDto> EndSessionAsync(int sessionId, CancellationToken ct = default) =>
        PostAsync<SessionDto>($"api/sessions/{sessionId}/end", new { }, ct);

    public Task<AssistDto> CreateAssistAsync(int computerId, string message, CancellationToken ct = default) =>
        PostAsync<AssistDto>("api/assist", new { computerId, message }, ct);

    public void Dispose() => _http.Dispose();

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    // Adds the kiosk's headers to every request.
    private sealed class KioskHeaders : DelegatingHandler
    {
        private readonly string _key;
        private readonly Func<int> _computerId;

        public KioskHeaders(string key, Func<int> computerId)
        {
            _key = key;
            _computerId = computerId;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(_key)) request.Headers.Add("X-Kiosk-Key", _key);
            var id = _computerId();
            if (id > 0) request.Headers.Add("X-Kiosk-Computer-Id", id.ToString());
            return base.SendAsync(request, ct);
        }
    }

    // Turns an error response into an ApiException, using the API's { "message": "..." } text.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        if ((int)response.StatusCode == 401)
            throw new ApiException(401, "The tracker refused this PC: the kiosk key in kiosk.settings.json is missing or wrong.");

        var message = $"The tracker returned an error ({(int)response.StatusCode}).";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var m) &&
                m.ValueKind == JsonValueKind.String)
            {
                message = m.GetString() ?? message;
            }
        }
        catch (JsonException)
        {
            // The error response had no JSON body; keep the generic message.
        }

        throw new ApiException((int)response.StatusCode, message);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter()); // "InUse" <-> ComputerStatus.InUse
        return options;
    }
}

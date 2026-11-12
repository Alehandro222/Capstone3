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

    // How far this PC's clock is from the tracker's (from each answer's Date header).
    // Countdowns use tracker time, so a PC whose clock is off still shows the right time left.
    // Differences under 3 seconds are ignored (the header only has whole seconds).
    public TimeSpan ServerClockOffset { get; private set; }

    // kioskKey and computerId are read on every request, because pairing changes them.
    // The API only accepts this PC's own key, and only for this PC.
    public ApiClient(string baseUrl, Func<string> kioskKey, Func<int> computerId)
        : this(baseUrl, kioskKey, computerId, new HttpClientHandler())
    {
    }

    // For the tests: "inner" stands in for the network.
    public ApiClient(string baseUrl, Func<string> kioskKey, Func<int> computerId, HttpMessageHandler inner)
    {
        var handler = new KioskHeaders(kioskKey, computerId) { InnerHandler = inner };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    // Trades a one-time pairing code (typed by staff) for this PC's key.
    public Task<KioskPairedDto> PairAsync(string code, CancellationToken ct = default) =>
        PostAsync<KioskPairedDto>("api/kiosk/pair", new { code }, ct);

    // "This PC is on" (every few seconds); answers with this PC and its session.
    public Task<KioskStateDto> HeartbeatAsync(CancellationToken ct = default) =>
        PostAsync<KioskStateDto>("api/kiosk/heartbeat", new { }, ct);

    // Staff-only registration on this PC: logs the visit and starts the session here.
    public Task<SessionDto> StartSessionAsync(KioskRegistration registration, CancellationToken ct = default) =>
        PostAsync<SessionDto>("api/kiosk/sessions", registration, ct);

    // reason: "Visitor" (Log out button) or "Idle" (automatic log out).
    public Task<SessionDto> EndSessionAsync(string reason, CancellationToken ct = default) =>
        PostAsync<SessionDto>("api/kiosk/session/end", new { reason }, ct);

    // Adds an hour, if the tracker's rules allow it (last 10 minutes, at most twice, nobody waiting).
    public Task<SessionDto> ExtendSessionAsync(CancellationToken ct = default) =>
        PostAsync<SessionDto>("api/kiosk/session/extend", new { }, ct);

    public Task<AssistDto> CreateAssistAsync(string message, CancellationToken ct = default) =>
        PostAsync<AssistDto>("api/kiosk/help", new { message }, ct);

    public void Dispose() => _http.Dispose();

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        if (response.Headers.Date is DateTimeOffset serverTime)
        {
            var offset = serverTime.UtcDateTime.AddMilliseconds(500) - DateTime.UtcNow;
            ServerClockOffset = offset.Duration() < TimeSpan.FromSeconds(3) ? TimeSpan.Zero : offset;
        }
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    // Adds the kiosk's headers to every request.
    private sealed class KioskHeaders : DelegatingHandler
    {
        private readonly Func<string> _key;
        private readonly Func<int> _computerId;

        public KioskHeaders(Func<string> key, Func<int> computerId)
        {
            _key = key;
            _computerId = computerId;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var key = _key();
            var id = _computerId();
            if (!string.IsNullOrEmpty(key) && id > 0)
            {
                request.Headers.Add("X-Kiosk-Key", key);
                request.Headers.Add("X-Kiosk-Computer-Id", id.ToString());
            }
            return base.SendAsync(request, ct);
        }
    }

    // Turns an error response into an ApiException, using the API's { "message": "..." } text.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        if ((int)response.StatusCode == 401)
            throw new ApiException(401, "The tracker no longer accepts this PC. Staff need to pair it again.");

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

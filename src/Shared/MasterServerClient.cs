using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlopArena.Shared
{
    /// <summary>HTTP client for the master server; carries one application JWT.</summary>
    public class MasterServerClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly HttpClient _feedbackHttp;
        private readonly bool _ownsHttpClient;

        private static readonly JsonSerializerOptions ServerJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        private string? _token;
        private long? _steamId;
        private DateTimeOffset? _expiresAt;
        private bool _steamAuthenticated;
        private int _sessionVersion;

        /// <summary>JWT bearer token for the current application session.</summary>
        public string? Token => _token;

        /// <summary>Verified Steam identity (or development guest identity).</summary>
        public long? SteamId => _steamId;

        /// <summary>UTC expiry returned by the master for the current token.</summary>
        public DateTimeOffset? TokenExpiresAt => _expiresAt;

        public bool IsAuthenticated => !string.IsNullOrEmpty(_token);
        public bool IsSteamAuthenticated => IsAuthenticated && _steamAuthenticated;

        /// <summary>HTTP status from the latest failed request, when available.</summary>
        public int? LastStatusCode { get; private set; }

        /// <summary>
        /// Create a client pointing at the given master server URL.
        /// </summary>
        public MasterServerClient(string masterServerUrl = "http://localhost:5000")
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(masterServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _feedbackHttp = new HttpClient { BaseAddress = _http.BaseAddress, Timeout = TimeSpan.FromSeconds(20) };
            _ownsHttpClient = true;
        }

        /// <summary>
        /// Create a client with a pre-configured HttpClient (for testing or DI).
        /// The caller owns the HttpClient lifetime.
        /// </summary>
        public MasterServerClient(HttpClient httpClient)
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _feedbackHttp = httpClient;
            _ownsHttpClient = false;
        }
        /// <summary>Authenticate with a fresh Steam web ticket, encoded as hex.</summary>
        public async Task<bool> AuthenticateSteamAsync(string ticket, CancellationToken ct = default)
        {
            if (IsAuthenticated || string.IsNullOrWhiteSpace(ticket))
                return false;
            return await ApplyAuthAsync("auth/steam", ticket, null, ct, steamLogin: true);
        }

        private async Task<bool> ApplyAuthAsync(string path, string? ticket, long? expectedSteamId,
            CancellationToken ct, bool steamLogin = false)
        {
            int version = _sessionVersion;
            try
            {
                LastStatusCode = null;
                using var content = ticket == null ? null : new StringContent(
                    JsonSerializer.Serialize(new SteamAuthPayload(ticket), ServerJsonOptions),
                    Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(path, content, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return false;
                var auth = JsonSerializer.Deserialize<GuestAuthResponse>(
                    await response.Content.ReadAsStringAsync(), ServerJsonOptions);
                if (version != _sessionVersion || auth == null || string.IsNullOrEmpty(auth.Token) ||
                    auth.SteamId <= 0 || (expectedSteamId.HasValue && auth.SteamId != expectedSteamId.Value))
                    return false;
                _token = auth.Token;
                _steamId = auth.SteamId;
                _expiresAt = auth.ExpiresAt == default ? null : auth.ExpiresAt;
                if (steamLogin)
                    _steamAuthenticated = true;
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch { return false; }
        }

        /// <summary>
        /// Authenticate as a fresh guest: POST /auth/guest. The returned
        /// identity is intentionally new for each game launch.
        /// </summary>
        public Task<bool> AuthenticateGuestAsync(CancellationToken ct = default) =>
            AuthenticateGuestCoreAsync(ct);

        /// <summary>
        /// Authenticate a fresh guest and apply its chosen display name before
        /// returning. This convenience overload still performs exactly one
        /// guest creation request.
        /// </summary>
        public async Task<bool> AuthenticateGuestAsync(string? displayName, CancellationToken ct = default)
        {
            if (!await AuthenticateGuestCoreAsync(ct))
                return false;
            return string.IsNullOrWhiteSpace(displayName) ||
                await SetDisplayNameAsync(displayName, ct) != null;
        }

        private async Task<bool> AuthenticateGuestCoreAsync(CancellationToken ct)
        {
            if (IsAuthenticated)
                return true;
            try
            {
                LastStatusCode = null;
                using var response = await _http.PostAsync("auth/guest", content: null, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return false;

                var json = await response.Content.ReadAsStringAsync();
                var auth = JsonSerializer.Deserialize<GuestAuthResponse>(json, ServerJsonOptions);
                if (auth == null || string.IsNullOrEmpty(auth.Token) || auth.SteamId <= 0)
                    return false;

                _token = auth.Token;
                _steamId = auth.SteamId;
                _expiresAt = auth.ExpiresAt == default ? null : auth.ExpiresAt;
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Apply or replace the authenticated guest's display name:
        /// PUT /auth/name. A null result represents validation, membership,
        /// authentication, network, or server failure.
        /// </summary>
        public async Task<GuestUserInfo?> SetDisplayNameAsync(
            string displayName, CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;
            try
            {
                LastStatusCode = null;
                var payload = JsonSerializer.Serialize(
                    new SetDisplayNamePayload(displayName ?? string.Empty), ServerJsonOptions);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.PutAsync("auth/name", content, ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;
                var json = await response.Content.ReadAsStringAsync();
                // PUT /auth/name returns the chat profile ({playerId,
                // displayName, sessionTag}), while /auth/me uses its legacy
                // ({steamId, username, ...}) shape. Normalize both at this
                // HTTP boundary so callers never lose the identity.
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (!root.TryGetProperty("playerId", out var playerId) ||
                    !root.TryGetProperty("displayName", out var name) ||
                    !root.TryGetProperty("sessionTag", out var tag) ||
                    playerId.ValueKind != JsonValueKind.String ||
                    name.ValueKind != JsonValueKind.String ||
                    tag.ValueKind != JsonValueKind.String)
                    return null;
                var rawPlayerId = playerId.GetString() ?? string.Empty;
                if (rawPlayerId.Length == 0)
                    return null;
                long.TryParse(rawPlayerId, out var steamId);
                if (steamId <= 0)
                    steamId = _steamId ?? 0;
                return new GuestUserInfo
                {
                    SteamId = steamId,
                    PlayerId = rawPlayerId,
                    Username = name.GetString() ?? string.Empty,
                    SessionTag = tag.GetString() ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Renew the same account with a fresh Steam ticket; development guests use no ticket.</summary>
        public Task<bool> RefreshAsync(string? freshSteamTicket, CancellationToken ct = default)
        {
            if (!IsAuthenticated || !_steamId.HasValue || (_steamAuthenticated && string.IsNullOrWhiteSpace(freshSteamTicket)))
                return Task.FromResult(false);
            return ApplyAuthAsync("auth/refresh", _steamAuthenticated ? freshSteamTicket : null, _steamId, ct);
        }

        public Task<bool> RefreshAsync(CancellationToken ct = default) => RefreshAsync(null, ct);

        /// <summary>
        /// Fetch the current user's info: GET /auth/me (requires prior auth).
        /// Returns null if not authenticated, network error, or non-2xx response.
        /// </summary>
        public async Task<GuestUserInfo?> GetMeAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;

            try
            {
                LastStatusCode = null;
                using var response = await _http.GetAsync("auth/me", ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GuestUserInfo>(json, ServerJsonOptions);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fetch the server browser list: GET /servers (requires prior auth).
        /// Returns null if not authenticated, network error, or non-2xx response.
        /// Returns an empty list if the server is up but has no servers to list.
        /// </summary>
        public async Task<List<ServerInfo>?> GetServersAsync(CancellationToken ct = default)
        {
            if (!IsAuthenticated)
                return null;

            try
            {
                LastStatusCode = null;
                using var response = await _http.GetAsync("servers", ct);
                LastStatusCode = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<ServerInfo>>(json, ServerJsonOptions);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        public async Task<FeedbackSendResult> SendFeedbackAsync(string jwt, FeedbackSubmission submission,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(jwt)) return new FeedbackSendResult.Retryable("Steam sign-in is unavailable. Try again.");
            int session = _sessionVersion;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                using var multipart = new MultipartFormDataContent();
                var metadata = JsonSerializer.Serialize(new FeedbackWire(submission), ServerJsonOptions);
                if (Encoding.UTF8.GetByteCount(metadata) > 16384) return new FeedbackSendResult.Rejected("feedback-too-large");
                multipart.Add(new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata");
                if (submission.Image != null)
                {
                    var image = new ByteArrayContent(submission.Image.Jpeg.ToArray());
                    image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    multipart.Add(image, "screenshot", "feedback.jpg");
                }
                using var request = new HttpRequestMessage(HttpMethod.Post, "feedback") { Content = multipart };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
                using var response = await _feedbackHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (session != _sessionVersion) return new FeedbackSendResult.Retryable("Steam session changed.");
                int status = (int)response.StatusCode;
                if (status == 200 || status == 201)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var bytes = new System.IO.MemoryStream();
                    var buffer = new byte[1024];
                    int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token)) > 0)
                    {
                        if (bytes.Length + count > 8192) return new FeedbackSendResult.Retryable("Couldn't confirm it arrived. Press Retry, it won't send twice.");
                        bytes.Write(buffer, 0, count);
                    }
                    using var document = JsonDocument.Parse(bytes.ToArray());
                    var root = document.RootElement;
                    if (root.TryGetProperty("reportId", out var id) && id.TryGetGuid(out var reportId) && reportId != Guid.Empty &&
                        root.TryGetProperty("receivedAtUtc", out var time) && time.TryGetDateTimeOffset(out var received))
                        return new FeedbackSendResult.Accepted(new FeedbackReceipt(reportId, received));
                    return new FeedbackSendResult.Retryable("Couldn't confirm it arrived. Press Retry, it won't send twice.");
                }
                if (status == 404) return new FeedbackSendResult.Retryable("Feedback isn't available yet.");
                if (status == 401) return new FeedbackSendResult.Retryable("Steam sign-in expired. Try again.");
                if (status == 429)
                {
                    var retry = response.Headers.RetryAfter;
                    var delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow);
                    if (delay.HasValue) delay = TimeSpan.FromSeconds(Math.Max(0, Math.Min(86400, delay.Value.TotalSeconds)));
                    return new FeedbackSendResult.Retryable("Too many reports for now. Try again later.", delay);
                }
                if (status >= 500 || status == 408) return new FeedbackSendResult.Retryable("The server is busy. Your feedback is kept, press Retry.");
                return new FeedbackSendResult.Rejected(status == 413 ? "feedback-too-large" :
                    status == 415 ? "unsupported-image" : status == 422 ? "unsupported-feedback-version" : "invalid-feedback");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is JsonException || e is System.IO.IOException || e is InvalidOperationException)
            { return new FeedbackSendResult.Retryable("Couldn't reach the server. Your feedback is kept, press Retry."); }
        }

        private sealed class FeedbackWire
        {
            public int SchemaVersion => 1;
            public Guid SubmissionId { get; }
            public string Kind { get; }
            public string Message { get; }
            public object Context { get; }
            public FeedbackWire(FeedbackSubmission submission)
            {
                SubmissionId = submission.SubmissionId; Kind = submission.Kind.ToString().ToLowerInvariant(); Message = submission.Message;
                var c = submission.Context;
                Context = new { c.AppVersion, c.ProtocolVersion, c.Platform, c.Scene,
                    Phase = c.Phase.ToString().ToLowerInvariant(),
                    ObservedAtUtc = c.ObservedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
                    Match = c.Match == null ? null : new { c.Match.Mode, c.Match.Format, c.Match.StageId,
                        c.Match.OnlineMatchId, c.Match.LocalEntityId, c.Match.LastObservedServerTick, c.Match.Fighters } };
            }
        }

        public void ClearAuthentication()
        {
            _sessionVersion++;
            _token = null;
            _steamId = null;
            _expiresAt = null;
            _steamAuthenticated = false;
            _http.DefaultRequestHeaders.Authorization = null;
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
            {
                _http.Dispose();
                _feedbackHttp.Dispose();
            }
        }

        private sealed class SetDisplayNamePayload
        {
            public SetDisplayNamePayload(string displayName) => DisplayName = displayName;
            public string DisplayName { get; }
        }

        private sealed class SteamAuthPayload
        {
            public SteamAuthPayload(string ticket) => Ticket = ticket;
            public string Ticket { get; }
        }
    }
}

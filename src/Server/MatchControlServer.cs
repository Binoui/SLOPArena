using System.Globalization;
using System.Net;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// Private Master-to-GameHost control endpoint. Explicit development profile
    /// allocates a match UDP port; VPS profile admits protocol-2 matches on the
    /// single process-wide Steam P2P listener and returns the canonical content map.
    ///
    /// VPS requests authenticate with a separate match-control key before the
    /// bounded body is read. The control listener must remain private.
    /// </summary>
    public sealed class MatchControlServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly SteamGameServerHost? _steamHost;
        private readonly string _defaultArena;
        private readonly byte[]? _controlKey;
        private readonly Func<bool> _isReady;
        private const int MaxBodyBytes = 64 * 1024;
        private CancellationTokenSource? _cts;
        private bool _disposed;
        private sealed record StartOutcome(int Port, MatchContentHandleMap? Content, string? Error,
            string? MatchId = null, string? ServerSteamId = null, string? ContentHash = null);

        public MatchControlServer(MultiMatchOrchestrator orchestrator, int port, string defaultArena,
            string? controlKey = null, Func<bool>? isReady = null, SteamGameServerHost? steamHost = null)
        {
            _orchestrator = orchestrator;
            _steamHost = steamHost;
            _defaultArena = defaultArena;
            _controlKey = controlKey is null ? null : Encoding.UTF8.GetBytes(controlKey);
            _isReady = isReady ?? (() => false);
            if (_controlKey?.Length > 4096)
                throw new ArgumentException("Control key is too long.", nameof(controlKey));
            _listener.Prefixes.Add($"http://*:{port}/");
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _ = Task.Run(() => RunAsync(_cts.Token));
            Console.WriteLine($"[MatchControl] Listening for match-start on TCP port {_listener.Prefixes.First()}");
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }

                try
                {
                    await HandleAsync(ctx);
                }
                catch (Exception)
                {
                    Console.WriteLine("[MatchControl] Match-start handler failed.");
                    try { ctx.Response.StatusCode = 500; } catch { }
                }
                finally
                {
                    try { ctx.Response.Close(); } catch { }
                }
            }
        }

        /// <summary>
        /// Pure handler (extracted for testability): parse + assign, no socket
        /// I/O. Returns the assigned UDP port (>=1) or 0 on a bad request, with a
        /// human-readable error. The caller is responsible for writing the HTTP
        /// response. Throws on an internal error (orchestrator failure).
        /// </summary>
        public (int port, MatchContentHandleMap? content, string? error) TryStartMatchWithContent(string jsonBody)
        {
            var result = TryStartMatch(jsonBody);
            return (result.Port, result.Content, result.Error);
        }

        private StartOutcome TryStartMatch(string jsonBody)
        {
            MatchStartRequest? request;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(jsonBody);
                request = MatchStartRequestCodec.TryParse(document.RootElement);
            }
            catch (JsonException) { return new StartOutcome(0, null, "Malformed JSON body."); }
            using (document)
            {
                if (request is null)
                    return new StartOutcome(0, null, "Invalid match-start body.");

                bool steam = _orchestrator.IsVps;
                Guid matchGuid = Guid.Empty;
                ulong serverSteamId = 0;
                if (steam)
                {
                    var now = DateTimeOffset.UtcNow;
                    serverSteamId = _steamHost?.CurrentSteamId ?? 0;
                    if (_steamHost is null || serverSteamId == 0 || !_steamHost.IsReady ||
                        request.ProtocolVersion != SteamMatchDescriptor.CurrentProtocolVersion ||
                        !Guid.TryParseExact(request.MatchId, "D", out matchGuid) || matchGuid == Guid.Empty ||
                        request.AdmissionExpiresAtUtc is not DateTimeOffset expiry ||
                        expiry <= now || expiry > now.AddSeconds(SteamMatchDescriptor.MaximumAdmissionWindowSeconds) ||
                        !document.RootElement.TryGetProperty("admissionExpiresAtUtc", out var deadline) ||
                        deadline.ValueKind != JsonValueKind.String ||
                        deadline.GetString() is not string deadlineText ||
                        !(deadlineText.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                          deadlineText.EndsWith("+00:00", StringComparison.Ordinal)))
                        return new StartOutcome(0, null, "Invalid or expired Steam match admission.");
                    if (string.IsNullOrWhiteSpace(request.ArenaName))
                        return new StartOutcome(0, null, "Steam matches require an arena name.");
                }
                else if (request.ProtocolVersion != 0)
                    return new StartOutcome(0, null, "Development matches require the explicit UDP protocol.");
                if (!steam && request.AdmissionExpiresAtUtc is { } developmentExpiry &&
                    (developmentExpiry <= DateTimeOffset.UtcNow ||
                     developmentExpiry > DateTimeOffset.UtcNow.AddSeconds(SteamMatchDescriptor.MaximumAdmissionWindowSeconds)))
                    return new StartOutcome(0, null, "Invalid or expired development match admission.");

                var arena = string.IsNullOrEmpty(request.ArenaName) ? _defaultArena : request.ArenaName;
                if (ArenaRegistry.Get(arena) is not ArenaDefinition selectedArena || !request.Format.Admits(selectedArena))
                    return new StartOutcome(0, null, $"Unknown arena '{arena}' for {request.Format.ToWire()}.");
                if (steam && (!_orchestrator.TryRefreshCatalogHash(out _) ||
                    !string.Equals(request.CatalogHash, _orchestrator.CatalogHash, StringComparison.Ordinal) ||
                    !_isReady()))
                    return new StartOutcome(0, null, "Master-pinned catalog or GameHost registration is stale.");
                if (!_orchestrator.TryAssignMatch(request.MatchId, arena, request.Players, (byte)request.MaxStocks,
                    request.AdmissionExpiresAtUtc,
                    steam ? request.CatalogHash : null,
                    out int port, out var content, out var contentHash, out var error, request.Format, request.FriendlyFire, request.BallOnly))
                    return new StartOutcome(0, null, error ?? "Failed to build match content.");
                if (steam && _steamHost!.CurrentSteamId != serverSteamId)
                {
                    _orchestrator.AbortMatch(matchGuid);
                    return new StartOutcome(0, null, "Steam game-server identity changed during allocation.");
                }

                return steam
                    ? new StartOutcome(port, content, null, matchGuid.ToString("D"),
                        serverSteamId.ToString(CultureInfo.InvariantCulture), contentHash)
                    : new StartOutcome(port, content, null, ContentHash: contentHash);
            }
        }

        public bool TryAbortMatch(string jsonBody)
        {
            try
            {
                using var document = JsonDocument.Parse(jsonBody);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("matchId", out var value) ||
                    value.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(value.GetString(), out var matchId) || matchId == Guid.Empty)
                    return false;
                _orchestrator.AbortMatch(matchId);
                return true;
            }
            catch (JsonException) { return false; }
        }

        private static bool IsAuthorized(string? authorization, byte[] controlKey)
        {
            if (authorization is null || authorization.Length > 4103 ||
                !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return false;
            var token = authorization.AsSpan(7).Trim();
            if (Encoding.UTF8.GetByteCount(token) != controlKey.Length)
                return false;
            Span<byte> candidate = stackalloc byte[controlKey.Length];
            Encoding.UTF8.GetBytes(token, candidate);
            return CryptographicOperations.FixedTimeEquals(candidate, controlKey);
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/health")
            {
                ctx.Response.StatusCode = 200;
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/ready")
            {
                ctx.Response.StatusCode = _isReady() ? 200 : 503;
                return;
            }
            string? path = ctx.Request.Url?.AbsolutePath;
            const string diagnosticsPrefix = "/match/diagnostics/";
            if (ctx.Request.HttpMethod == "GET" && path?.StartsWith(diagnosticsPrefix, StringComparison.Ordinal) == true)
            {
                ctx.Response.Headers["Cache-Control"] = "no-store";
                // Diagnostics always require the private credential, including development.
                if (_controlKey is null || !IsAuthorized(ctx.Request.Headers["Authorization"], _controlKey))
                { ctx.Response.StatusCode = 401; return; }
                if (!Guid.TryParseExact(path[diagnosticsPrefix.Length..], "D", out var matchId) || matchId == Guid.Empty)
                { ctx.Response.StatusCode = 404; return; }
                var diagnostics = _orchestrator.Diagnostics.Get(matchId);
                if (diagnostics is null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(diagnostics,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return;
            }
            bool abort = path == "/match/abort";
            if (ctx.Request.HttpMethod != "POST" || path != "/match/start" && !abort)
            {
                ctx.Response.StatusCode = 404;
                return;
            }
            if (!abort && _steamHost is not null && !_isReady())
            {
                ctx.Response.StatusCode = 503;
                return;
            }

            if (_controlKey is not null && !IsAuthorized(ctx.Request.Headers["Authorization"], _controlKey))
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            if (ctx.Request.ContentLength64 > MaxBodyBytes)
            {
                ctx.Response.StatusCode = 413;
                return;
            }

            var bytes = ArrayPool<byte>.Shared.Rent(MaxBodyBytes + 1);
            string body;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                int length = 0, read;
                do
                {
                    read = await ctx.Request.InputStream.ReadAsync(
                        bytes.AsMemory(length, MaxBodyBytes + 1 - length), timeout.Token);
                    length += read;
                } while (read != 0 && length <= MaxBodyBytes);
                if (length > MaxBodyBytes)
                {
                    ctx.Response.StatusCode = 413;
                    return;
                }
                body = new UTF8Encoding(false, true).GetString(bytes, 0, length);
            }
            catch (OperationCanceledException)
            {
                ctx.Response.StatusCode = 408;
                return;
            }
            catch (DecoderFallbackException)
            {
                ctx.Response.StatusCode = 400;
                return;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }

            ctx.Response.ContentType = "application/json";
            if (abort)
            {
                ctx.Response.StatusCode = TryAbortMatch(body) ? 200 : 400;
                await ctx.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"aborted\":true}"));
                return;
            }

            var result = TryStartMatch(body);
            if (result.Error is not null)
            {
                ctx.Response.StatusCode = 400;
                await ctx.Response.OutputStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(new { error = result.Error }));
                return;
            }
            if (result.ServerSteamId is not null)
            {
                var steamResponse = Encoding.UTF8.GetBytes(
                    $"{{\"matchId\":\"{result.MatchId}\",\"serverSteamId\":\"{result.ServerSteamId}\",\"virtualPort\":0,\"protocolVersion\":{SteamMatchDescriptor.CurrentProtocolVersion},\"content\":{MatchContentHandleMapCodec.Serialize(result.Content!)},\"contentHash\":\"{result.ContentHash}\"}}");
                await ctx.Response.OutputStream.WriteAsync(steamResponse);
            }
            else
            {
                var udpResponse = Encoding.UTF8.GetBytes(
                    $"{{\"port\":{result.Port},\"content\":{MatchContentHandleMapCodec.Serialize(result.Content!)},\"contentHash\":\"{result.ContentHash}\"}}");
                await ctx.Response.OutputStream.WriteAsync(udpResponse);
            }
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _cts?.Dispose();
            ((IDisposable)_listener).Dispose();
        }
    }
}

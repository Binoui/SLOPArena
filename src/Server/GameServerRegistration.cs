using System.Collections.Concurrent;
using System.Net;
using System.Globalization;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// Owns one GameServer registration session: retries transient startup outages,
    /// heartbeats the Master, re-registers if the record is lost, and reports
    /// completed matches with the returned API token.
    /// </summary>
    public class GameServerRegistration
    {
        private readonly HttpClient _http;
        private readonly ServerConfig _config;
        private readonly MultiMatchOrchestrator _orchestrator;
        private readonly Func<ulong>? _getSteamId;
        private readonly ConcurrentDictionary<Guid, PendingReport> _pendingReports = new();
        private readonly SemaphoreSlim _reportGate = new(1, 1);
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly TimeProvider _clock;
        private static readonly TimeSpan RegistrationFreshness = TimeSpan.FromSeconds(15);
        private RegistrationSession? _session;
        private int _running;
        private sealed record RegistrationSession(Guid Id, string Token, ulong? SteamId, Guid InstanceId, string CatalogHash, long AcknowledgedAt);
        private sealed record PendingReport(Guid MatchId, MatchResultReport? Result, string? CancellationReason);
        private enum RegistrationOutcome { Success, Retry, Fatal }

        public Guid InstanceId { get; } = Guid.NewGuid();
        public Guid ServerId => Volatile.Read(ref _session)?.Id ?? Guid.Empty;
        public bool IsRegistered => Volatile.Read(ref _session) is not null;
        public bool HasFreshRegistration
        {
            get
            {
                var session = Volatile.Read(ref _session);
                if (session is null || _clock.GetElapsedTime(session.AcknowledgedAt) >= RegistrationFreshness)
                    return false;
                return !_config.IsVps || session.InstanceId == InstanceId &&
                    session.SteamId is ulong registered && registered != 0 &&
                    _getSteamId?.Invoke() == registered &&
                    string.Equals(session.CatalogHash, _orchestrator.CatalogHash, StringComparison.Ordinal);
            }
        }
        public bool CanAdmitMatches => HasFreshRegistration &&
            _pendingReports.Count + _orchestrator.CurrentMatchCount < Math.Max(16, _config.MaxConcurrentMatches * 4);


        public GameServerRegistration(ServerConfig config, MultiMatchOrchestrator orchestrator,
            HttpMessageHandler? handler = null, TimeProvider? clock = null, Func<ulong>? getSteamId = null)
        {
            _config = config;
            _orchestrator = orchestrator;
            _getSteamId = getSteamId;
            _http = new HttpClient(handler ?? new HttpClientHandler())
            {
                BaseAddress = new Uri(config.MasterServerUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>Single registration and heartbeat owner; retries transient outages until cancelled.</summary>
        public async Task RunAsync(CancellationToken ct, TimeSpan? heartbeatInterval = null, TimeSpan? initialRetryDelay = null)
        {
            var heartbeat = heartbeatInterval ?? TimeSpan.FromSeconds(10);
            var firstRetry = initialRetryDelay ?? TimeSpan.FromSeconds(1);
            if (Interlocked.Exchange(ref _running, 1) != 0)
                throw new InvalidOperationException("Registration loop is already running.");
            var retry = firstRetry;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _session) is null)
                    {
                        var outcome = await TryRegisterAsync(ct);
                        if (outcome == RegistrationOutcome.Fatal)
                            throw new InvalidOperationException("GameServer registration rejected; check the deployment profile and approved host credential.");
                        if (outcome == RegistrationOutcome.Success)
                        {
                            retry = firstRetry;
                            continue;
                        }
                        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 251));
                        await Task.Delay(retry + jitter, ct);
                        retry = TimeSpan.FromMilliseconds(Math.Min(retry.TotalMilliseconds * 2, 30_000));
                    }
                    else
                    {
                        await Task.Delay(heartbeat, ct);
                        await SendHeartbeatAsync(ct);
                        await DrainPendingReportsAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            finally
            {
                _orchestrator.Shutdown("host_restart");
                using var shutdownDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await DrainPendingReportsAsync(shutdownDeadline.Token); }
                catch (OperationCanceledException) when (shutdownDeadline.IsCancellationRequested) { }
                await DeregisterAsync(shutdownDeadline.Token);
                Volatile.Write(ref _running, 0);
            }
        }

        private async Task<RegistrationOutcome> TryRegisterAsync(CancellationToken ct)
        {
            try
            {
                ulong currentSteamId = _getSteamId?.Invoke() ?? 0;
                bool catalogReady = _orchestrator.TryRefreshCatalogHash(out _);
                if (_config.IsVps && (currentSteamId == 0 || !catalogReady))
                    return RegistrationOutcome.Retry;
                string? catalogHash = catalogReady ? _orchestrator.CatalogHash : null;
                if (_config.IsVps && catalogHash is null)
                    return RegistrationOutcome.Retry;
                var ip = _config.PublicIp ?? GetPublicIpAddress();
                var payload = new
                {
                    hostId = _config.HostId,
                    instanceId = _config.IsVps ? InstanceId : (Guid?)null,
                    steamId = _config.IsVps ? currentSteamId.ToString(CultureInfo.InvariantCulture) : null,
                    protocolVersion = _config.IsVps ? SteamMatchDescriptor.CurrentProtocolVersion : 0,
                    catalogHash = catalogHash,
                    name = _config.ServerName,
                    ipAddress = ip,
                    port = _config.Port,
                    region = _config.Region,
                    isOfficial = _config.IsOfficial,
                    maxConcurrentMatches = _config.MaxConcurrentMatches,
                    customRulesJson = _config.CustomRules != null
                        ? JsonSerializer.Serialize(_config.CustomRules, _jsonOptions)
                        : null
                };
                using var request = new HttpRequestMessage(HttpMethod.Post, "servers/register")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions), Encoding.UTF8, "application/json")
                };
                if (_config.IsVps)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.RegistrationKey);
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Registration] Rejected: {response.StatusCode}");
                    return (int)response.StatusCode is 408 or 429 or >= 500
                        ? RegistrationOutcome.Retry : RegistrationOutcome.Fatal;
                }

                var result = JsonSerializer.Deserialize<RegistrationResponse>(
                    await response.Content.ReadAsStringAsync(ct), _jsonOptions);
                if (result == null || result.ServerId == Guid.Empty || string.IsNullOrWhiteSpace(result.ApiToken))
                    return RegistrationOutcome.Fatal;
                Volatile.Write(ref _session, new RegistrationSession(result.ServerId, result.ApiToken,
                    _config.IsVps ? currentSteamId : null, InstanceId, catalogHash ?? string.Empty, _clock.GetTimestamp()));
                Console.WriteLine($"[Registration] Registered (ID: {result.ServerId}, public address: {ip})");
                return RegistrationOutcome.Success;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (HttpRequestException)
            {
                Console.WriteLine("[Registration] Master unavailable; retrying.");
                return RegistrationOutcome.Retry;
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Registration] Master timed out; retrying.");
                return RegistrationOutcome.Retry;
            }
        }

        /// <summary>Best-effort removal of the current browser record during shutdown.</summary>
        private async Task DeregisterAsync(CancellationToken ct)
        {
            var session = Volatile.Read(ref _session);
            if (session is null) return;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"servers/{session.Id}");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"[Registration] Deregistered (ID: {session.Id}).");
                else
                    Console.WriteLine($"[Registration] Deregistration failed: {response.StatusCode}");
            }
            catch (Exception)
            {
                Console.WriteLine("[Registration] Deregistration unavailable.");
            }
            finally
            {
                Interlocked.CompareExchange(ref _session, null, session);
            }
        }

        private async Task SendHeartbeatAsync(CancellationToken ct)
        {
            var session = Volatile.Read(ref _session);
            if (session is null) return;
            ulong steamId = _getSteamId?.Invoke() ?? 0;
            bool catalogReady = _orchestrator.TryRefreshCatalogHash(out _);
            string? catalogHash = _orchestrator.CatalogHash;
            if (_config.IsVps && (steamId == 0 || !catalogReady || catalogHash is null)) return;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"servers/{session.Id}/heartbeat")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    currentMatches = _orchestrator.CurrentMatchCount,
                    instanceId = _config.IsVps ? InstanceId : (Guid?)null,
                    steamId = _config.IsVps ? steamId.ToString(CultureInfo.InvariantCulture) : null,
                    protocolVersion = _config.IsVps ? (int?)SteamMatchDescriptor.CurrentProtocolVersion : null,
                    catalogHash
                }),
                    Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Conflict)
                {
                    Console.WriteLine("[Heartbeat] Master registration identity or catalog changed; reconnecting.");
                    Interlocked.CompareExchange(ref _session, null, session);
                }
                else if (!response.IsSuccessStatusCode)
                    Console.WriteLine($"[Heartbeat] Master unavailable: {response.StatusCode}");
                else
                    Interlocked.CompareExchange(ref _session,
                        session with { CatalogHash = catalogHash ?? string.Empty, AcknowledgedAt = _clock.GetTimestamp() }, session);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (HttpRequestException)
            {
                Console.WriteLine("[Heartbeat] Master unavailable.");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("[Heartbeat] Master timed out.");
            }
        }

        public Task ReportMatchResultAsync(MatchResultReport result, CancellationToken ct = default)
        {
            if (result.MatchId != Guid.Empty)
                _pendingReports.TryAdd(result.MatchId, new PendingReport(result.MatchId, result, null));
            return DrainPendingReportsAsync(ct);
        }

        public void QueueMatchCancellation(Guid matchId, string reason)
        {
            if (matchId == Guid.Empty || reason is not ("unfilled" or "absent" or "host_restart" or "host_shutdown" or "content_unavailable"
                    or MatchInstance.MatchFailedReason))
                return;
            _pendingReports.TryAdd(matchId, new PendingReport(matchId, null, reason));
            _ = DrainPendingReportsAsync(CancellationToken.None);
        }

        private async Task DrainPendingReportsAsync(CancellationToken ct)
        {
            await _reportGate.WaitAsync(ct);
            try
            {
                foreach (var report in _pendingReports.Values)
                {
                    var session = Volatile.Read(ref _session);
                    if (session is null) return;
                    string path = report.Result is null ? "match/cancel" : "match/result";
                    object payload = report.Result is { } result
                        ? new { matchId = report.MatchId, winnerSteamId = result.WinnerSteamId, players = result.Players }
                        : new { matchId = report.MatchId, reason = report.CancellationReason };
                    using var request = new HttpRequestMessage(HttpMethod.Post, path)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptions),
                            Encoding.UTF8, "application/json")
                    };
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Token);
                    try
                    {
                        using var response = await _http.SendAsync(request, ct);
                        if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
                            _pendingReports.TryRemove(report.MatchId, out _);
                        else if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            Interlocked.CompareExchange(ref _session, null, session);
                            return;
                        }
                        else if ((int)response.StatusCode is 408 or 429 or >= 500)
                        {
                            Console.WriteLine($"[MatchReport] Master unavailable: {response.StatusCode}");
                            return;
                        }
                        else
                        {
                            Console.WriteLine($"[MatchReport] Rejected: {response.StatusCode}");
                            _pendingReports.TryRemove(report.MatchId, out _);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (HttpRequestException)
                    {
                        Console.WriteLine("[MatchReport] Master unavailable; report retained for retry.");
                        return;
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine("[MatchReport] Master timed out; report retained for retry.");
                        return;
                    }
                }
            }
            finally { _reportGate.Release(); }
        }

        /// <summary>
        /// Determine the public IP of this machine.
        /// Tries to detect non-loopback IPv4, falls back to hostname resolution.
        /// </summary>
        private static string GetPublicIpAddress()
        {
            // Try to find a non-loopback, non-link-local IPv4 address
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address) &&
                        !addr.Address.ToString().StartsWith("169.254"))
                    {
                        return addr.Address.ToString();
                    }
                }
            }

            // Fallback: resolve hostname
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var addr in host.AddressList)
                {
                    if (addr.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr))
                    {
                        return addr.ToString();
                    }
                }
            }
            catch { }

            return "127.0.0.1";
        }

        private class RegistrationResponse
        {
            public Guid ServerId { get; set; }
            public string ApiToken { get; set; } = string.Empty;
        }
    }
}

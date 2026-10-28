using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.Server
{
    /// <summary>
    /// Orchestrates authoritative MatchInstance lifecycles.
    /// Development explicitly allocates per-match UDP ports; VPS matches route
    /// through the single process-wide SteamNetworkingSockets listener.
    /// </summary>
    public class MultiMatchOrchestrator
    {
        private readonly ConcurrentDictionary<int, MatchInstance> _activeMatches = new();
        private readonly ConcurrentDictionary<Guid, MatchInstance> _steamMatches = new();
        private readonly ServerConfig _config;
        private readonly MatchContentCatalogProvider? _contentProvider;
        private readonly SteamGameServerHost? _steamHost;
        private readonly object _admissionGate = new();
        private bool _stopping;
        private string? _catalogHash;

        public bool ContentReady { get; }
        public bool IsVps => _config.IsVps;
        public MatchDiagnosticsStore Diagnostics { get; } = new();
        public string? CatalogHash => Volatile.Read(ref _catalogHash);

        public MultiMatchOrchestrator(ServerConfig config, SteamGameServerHost? steamHost = null)
        {
            _config = config;
            _steamHost = steamHost;
            try
            {
                _contentProvider = new MatchContentCatalogProvider();
                ContentReady = _contentProvider.TryBuild(out _, out var content, out _) && content is not null;
                if (ContentReady)
                    Volatile.Write(ref _catalogHash, SteamMatchDescriptor.HashCatalog(content!));
            }
            catch (Exception)
            {
                Console.WriteLine("[Content] Cooked roster is unavailable.");
            }
        }

        public bool TryRefreshCatalogHash(out string? error)
        {
            error = null;
            if (_contentProvider is not null &&
                _contentProvider.TryBuild(out _, out var content, out error) && content is not null)
            {
                Volatile.Write(ref _catalogHash, SteamMatchDescriptor.HashCatalog(content));
                return true;
            }
            Volatile.Write(ref _catalogHash, null);
            error ??= "Cooked roster unavailable.";
            return false;
        }

        /// <summary>Optional callback invoked once with (match GUID, winner SteamID) when a match ends.</summary>
        public Action<Guid, long>? ReportMatchResult { get; set; }
        public Action<Guid, string>? ReportMatchCancellation { get; set; }

        /// <summary>Assigns a match only after building its match-scoped catalog.</summary>
        /// <param name="format">What the Match plays (ADR-0040); the arena must suit it.</param>
        public bool TryAssignMatch(string matchId, string arenaName, IReadOnlyList<MatchPlayer> roster, byte maxStocks,
            DateTimeOffset? admissionDeadlineUtc, string? expectedCatalogHash,
            out int port, out MatchContentHandleMap? content, out string? contentHash, out string? error,
            MatchFormat format = MatchFormat.Stock, FriendlyFireMode friendlyFire = FriendlyFireMode.Off, bool ballOnly = false)
        {
            port = -1; content = null; contentHash = null; error = null;
            lock (_admissionGate)
                if (_stopping) { error = "GameServer is shutting down."; return false; }
            if (roster == null || roster.Count is < 2 or > 4) { error = "Roster must contain 2-4 players."; return false; }
            if (roster.Select(p => p.EntityId).Distinct().Count() != roster.Count
                || (format == MatchFormat.SlopballVersus ? !SlopballCamps.AreBalanced(roster.Select(p => p.Camp))
                        || roster.Any(p => p.EntityId <= 0 || p.EntityId >= ServerSimulation.ArcadeBallTargetInputId)
                    : ballOnly || roster.Any(p => p.Camp != SlopballCamp.None))
                || !format.AdmitsFriendlyFire(friendlyFire))
            { error = "Invalid Slopball camps or format rules."; return false; }
            bool steamTransport = _config.IsVps;
            if (steamTransport && (_steamHost is null || !_steamHost.IsReady || admissionDeadlineUtc is null))
            {
                error = "Steam game-server listener or match deadline unavailable.";
                return false;
            }
            if (!ArenaRegistry.TryGetMatchArena(arenaName, out var selectedArena, out var arenaIdentity)
                || !format.Admits(selectedArena))
            {
                error = $"Arena '{arenaName}' is unavailable for {format.ToWire()}.";
                return false;
            }
            if (_contentProvider is null ||
                !_contentProvider.TryBuild(out var catalog, out content, out error) || catalog is null)
            {
                error ??= "Cooked roster unavailable.";
                return false;
            }
            if (format.IsCourse())
            {
                var courseArena = selectedArena;
                if (roster.Any(p => p.EntityId <= 0 || p.EntityId >= 128 || p.Colour > 3
                    || catalog.Resolve(p.CharacterClass) is not { } entry || !courseArena.Arcade!.AllowsPackage(entry.Identity.PackageId))
                    || roster.Select(p => p.Colour).Distinct().Count() != roster.Count)
                { error = "Course roster must use admitted fighters and distinct colours."; return false; }
            }
            string actualCatalogHash = SteamMatchDescriptor.HashCatalog(content!);
            Volatile.Write(ref _catalogHash, actualCatalogHash);
            if ((steamTransport || expectedCatalogHash != null) &&
                !string.Equals(expectedCatalogHash, actualCatalogHash, StringComparison.Ordinal))
            {
                error = "Master-pinned catalog hash is stale.";
                return false;
            }
            content = content!.WithArena(arenaIdentity!);
            contentHash = SteamMatchDescriptor.HashContent(content);
            Guid matchGuid = Guid.Empty;
            if (steamTransport && (!Guid.TryParseExact(matchId, "D", out matchGuid) || matchGuid == Guid.Empty))
            {
                error = "Steam match ID must be a non-empty GUID.";
                return false;
            }

            lock (_admissionGate)
            {
                if (_stopping) { error = "GameServer is shutting down."; return false; }
                if (_activeMatches.Count + _steamMatches.Count >= _config.MaxConcurrentMatches ||
                    _activeMatches.Values.Any(x => x.MatchId == matchId) ||
                    (steamTransport && _steamMatches.ContainsKey(matchGuid)))
                {
                    error = $"No capacity or duplicate match ID for match {matchId}.";
                    return false;
                }
                if (steamTransport)
                {
                    MatchInstance match;
                    try
                    {
                        match = new MatchInstance(0, matchId, selectedArena, roster, catalog,
                            _ => { }, maxStocks, ReportMatchResult, OnSteamMatchEnd, ReportMatchCancellation,
                            admissionDeadlineUtc, contentHash, _steamHost!.SendToMatchEntity,
                            contentHandleMap: content, steamFlush: _steamHost.SignalOutbound, format: format, friendlyFire: friendlyFire, ballOnly: ballOnly);
                    }
                    catch (Exception ex) { error = ex.Message; return false; }
                    if (!_steamMatches.TryAdd(matchGuid, match))
                    {
                        error = $"Match {matchId} is already active.";
                        return false;
                    }
                    Diagnostics.Add(match.Diagnostics);
                    match.Start();
                    port = SteamMatchDescriptor.GameplayVirtualPort;
                    Console.WriteLine($"[Orchestrator] Steam match {matchId} assigned ({_steamMatches.Count}/{_config.MaxConcurrentMatches}) — {roster.Count} players");
                    return true;
                }

                for (int offset = 0; offset < _config.MaxConcurrentMatches; offset++)
                {
                    int candidate = _config.Port + offset;
                    if (_activeMatches.ContainsKey(candidate)) continue;
                    MatchInstance match;
                    try
                    {
                        match = new MatchInstance(candidate, matchId, selectedArena, roster, catalog,
                            OnMatchEnd, maxStocks, ReportMatchResult, onMatchCancelled: ReportMatchCancellation,
                            admissionDeadlineUtc: admissionDeadlineUtc, contentHash: contentHash, contentHandleMap: content, format: format, friendlyFire: friendlyFire, ballOnly: ballOnly);
                    }
                    catch (Exception ex) { error = ex.Message; return false; }
                    if (_activeMatches.TryAdd(candidate, match))
                    {
                        Diagnostics.Add(match.Diagnostics);
                        match.Start();
                        port = candidate;
                        Console.WriteLine($"[Orchestrator] Match {matchId} assigned to UDP {port} ({_activeMatches.Count}/{_config.MaxConcurrentMatches}) — {roster.Count} players");
                        return true;
                    }
                }
                error = $"No ports available for match {matchId} (max {_config.MaxConcurrentMatches}).";
                return false;
            }
        }

        public bool TryAdmitSteamPlayer(Guid matchId, ulong steamId, long connectionId, string contentHash,
            out ulong entityId, out long replacedConnectionId, out byte denialCode)
        {
            entityId = 0;
            replacedConnectionId = 0;
            denialCode = 1;
            return _steamMatches.TryGetValue(matchId, out var match) &&
                match.TryBindSteamPlayer(steamId, connectionId, contentHash, out entityId, out replacedConnectionId, out denialCode);
        }

        public bool TryQueueSteamInput(Guid matchId, long connectionId, uint newestTick, ReadOnlySpan<InputState> inputs) =>
            _steamMatches.TryGetValue(matchId, out var match) && match.TryQueueSteamInput(connectionId, newestTick, inputs);

        public bool TryMarkSteamReady(Guid matchId, long connectionId, NetplayControlPacket control) =>
            _steamMatches.TryGetValue(matchId, out var match) && match.TryMarkSteamReady(connectionId, control);

        public void DisconnectSteamPlayer(Guid matchId, long connectionId)
        {
            if (_steamMatches.TryGetValue(matchId, out var match))
                match.DisconnectSteamPlayer(connectionId);
        }

        public void RecordSteamHealth(Guid matchId, ulong entityId, int? ping, float receiveQuality, float sendQuality)
        {
            if (_steamMatches.TryGetValue(matchId, out var match))
                match.Diagnostics.RecordPeer(entityId, true, ping, receiveQuality, sendQuality);
        }

        public bool AbortMatch(Guid matchId)
        {
            lock (_admissionGate)
            {
                if (_steamMatches.TryRemove(matchId, out var steamMatch))
                {
                    _steamHost?.CloseMatch(matchId);
                    steamMatch.Stop();
                    return true;
                }

                foreach (var entry in _activeMatches)
                {
                    var match = entry.Value;
                    if (!Guid.TryParse(match.MatchId, out var activeMatchId) || activeMatchId != matchId)
                        continue;
                    match.StopAndWait();
                    return true;
                }
                return false;
            }
        }

        public void StopAcceptingMatches()
        {
            lock (_admissionGate) _stopping = true;
        }

        /// <summary>Called by MatchInstance when a development UDP match ends.</summary>
        private void OnMatchEnd(int port)
        {
            if (_activeMatches.TryRemove(port, out _))
                Console.WriteLine($"[Orchestrator] Match on port {port} ended ({CurrentMatchCount}/{_config.MaxConcurrentMatches})");
        }

        private void OnSteamMatchEnd(Guid matchId)
        {
            if (_steamMatches.TryRemove(matchId, out _))
            {
                _steamHost?.CloseMatch(matchId);
                Console.WriteLine($"[Orchestrator] Steam match {matchId} ended ({CurrentMatchCount}/{_config.MaxConcurrentMatches})");
            }
        }

        public int CurrentMatchCount => _activeMatches.Count + _steamMatches.Count;

        /// <summary>Graceful shutdown — cancel matches and wait for their threads.</summary>
        public void Shutdown(string cancellationReason = "host_restart")
        {
            StopAcceptingMatches();
            Console.WriteLine("[Orchestrator] Shutting down...");
            foreach (var kv in _activeMatches) kv.Value.Stop();
            foreach (var kv in _steamMatches)
            {
                _steamHost?.CloseMatch(kv.Key);
                kv.Value.Stop(cancellationReason);
            }

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (CurrentMatchCount > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
                foreach (var kv in _activeMatches)
                    if (!kv.Value.IsRunning) _activeMatches.TryRemove(kv.Key, out _);
                foreach (var kv in _steamMatches)
                    if (!kv.Value.IsRunning) _steamMatches.TryRemove(kv.Key, out _);
            }
            Console.WriteLine("[Orchestrator] Shutdown complete.");
        }

    }

    /// <summary>
    /// Deserialized from server.json at startup.
    /// </summary>
    public class ServerConfig
    {
        public string DeploymentProfile { get; set; } = string.Empty;
        public Guid? HostId { get; set; }
        public string? RegistrationKey { get; set; }
        public string? MatchControlKey { get; set; }
        public string ServerName { get; set; } = "SLOPArena Server";
        public string Region { get; set; } = "EU";
        public int Port { get; set; }
        public int MaxConcurrentMatches { get; set; }
        public string MasterServerUrl { get; set; } = string.Empty;
        /// <summary>Public IP or DNS name advertised for gameplay UDP.</summary>
        public string? PublicIp { get; set; }
        public bool IsOfficial { get; set; }
        /// <summary>Directory containing .arena files relative to the working directory.</summary>
        public string ArenaDataDir { get; set; } = "data/arenas";
        public CustomRules? CustomRules { get; set; }

        public bool IsVps => DeploymentProfile == "vps";

        public void Validate()
        {
            if (DeploymentProfile is not ("vps" or "development"))
                throw new InvalidOperationException("Select deploymentProfile: vps or development explicitly.");
            if (Port is < 1 or > 65535 || MaxConcurrentMatches is < 1 or > 100 ||
                Port + MaxConcurrentMatches - 1 > 65535)
                throw new InvalidOperationException("Invalid GameServer port range.");
            if (!Uri.TryCreate(MasterServerUrl, UriKind.Absolute, out var master) ||
                master.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(master.UserInfo) ||
                !string.IsNullOrEmpty(master.Query) || !string.IsNullOrEmpty(master.Fragment) ||
                master.AbsolutePath != "/")
                throw new InvalidOperationException("Invalid masterServerUrl.");
            if (!IsVps) return;
            if (HostId is null || HostId == Guid.Empty ||
                string.IsNullOrWhiteSpace(PublicIp) ||
                Uri.CheckHostName(PublicIp) is not (UriHostNameType.IPv4 or UriHostNameType.Dns) ||
                !IsBearerCredential(RegistrationKey) || !IsBearerCredential(MatchControlKey) ||
                RegistrationKey == MatchControlKey)
                throw new InvalidOperationException("VPS host identity, public address and distinct credentials are required.");
        }

        private static bool IsBearerCredential(string? value)
        {
            if (value is null || value.Length is < 32 or > 4096) return false;
            bool padding = false;
            foreach (char c in value)
            {
                if (c == '=') { padding = true; continue; }
                if (padding || !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '+' or '/'))
                    return false;
            }
            return true;
        }

        public static ServerConfig Load(string path)
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Empty GameServer configuration.");
            config.Validate();
            return config;
        }
    }

    public class CustomRules
    {
        public string[]? AllowedCharacters { get; set; }
        public string[]? AllowedMaps { get; set; }
    }
}

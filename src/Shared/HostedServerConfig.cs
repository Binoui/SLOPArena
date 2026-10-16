using System.Text.Json;

namespace SlopArena.Shared;

/// <summary>
/// Pure, Unity-free description of the <c>server.json</c> the embedded
/// host-and-play flow (ADR-0005, issue #39) writes to a temp file before
/// spawning <c>SlopArena.Server</c> as a subprocess. The server's
/// <c>ServerConfig.Load</c> deserializes case-insensitively, so the emitted
/// camelCase keys map onto its PascalCase properties.
///
/// Kept in Shared (not <c>SlopArena.Server</c>) so the Unity client can build
/// it without referencing the server project, and so it is unit-testable from
/// <c>tests/Shared.Tests</c>.
/// </summary>
public sealed record HostedServerConfig
{
    /// <summary>Embedded hosting explicitly uses the local development profile.</summary>
    public string DeploymentProfile => "development";

    /// <summary>Display name in the server browser (e.g. "Binoui's Server").</summary>
    public string ServerName { get; init; } = "SLOPArena Hosted";

    /// <summary>Region tag advertised to the master server.</summary>
    public string Region { get; init; } = "EU";

    /// <summary>Base UDP port for match instances. The host connects here.</summary>
    public int Port { get; init; } = 9876;

    /// <summary>
    /// One match per host for the demo; the orchestrator allocates
    /// <c>port .. port + MaxConcurrentMatches - 1</c>.
    /// </summary>
    public int MaxConcurrentMatches { get; init; } = 1;

    /// <summary>Master server base URL (e.g. http://localhost:5000).</summary>
    public string MasterServerUrl { get; init; } = "http://localhost:5000";

    /// <summary>
    /// Public IP/domain for the browser list when the host machine is behind NAT.
    /// Null → the server advertises its LAN IP (LAN-only hosting).
    /// </summary>
    public string? PublicIp { get; init; }

    /// <summary>Hosted servers are never official.</summary>
    public bool IsOfficial { get; init; } = false;

    /// <summary>
    /// Absolute path to the directory holding <c>.arena</c> files. Must be
    /// absolute because the subprocess runs with a different working directory.
    /// </summary>
    public string ArenaDataDir { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>Serialize to the <c>server.json</c> text the server expects.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, s_jsonOptions);
}

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared.AI;

namespace SlopArena.Shared.Telemetry;

/// <summary>
/// Local practice-session log: gzip-compressed JSON Lines, one <c>{"type":…,"data":…}</c>
/// record per line. A session is one header, then frames, swings, hits and events in tick
/// order, and an end record when the session closed cleanly. Nothing here sends data anywhere;
/// Unity writes the file and offline tools read it.
/// </summary>
public static class SessionLogFormat
{
    public const int SchemaVersion = 1;
    public const string FileSuffix = ".slopsession.jsonl.gz";

    internal const string HeaderType = "session";
    internal const string FrameType = "frame";
    internal const string SwingType = "swing";
    internal const string HitType = "hit";
    internal const string EventType = "event";
    internal const string EndType = "end";

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Session identity, fighters and the presentation settings in force when it began.</summary>
public sealed class SessionHeader
{
    public int Schema = SessionLogFormat.SchemaVersion;
    public string StartedUtc = "";
    /// <summary>Client game mode, for example Training or Solo.</summary>
    public string Mode = "";
    public string Arena = "";
    public string AppVersion = "";
    public string EngineVersion = "";
    public string Platform = "";
    public bool Editor;
    public int TickRateHz = 60;
    public List<SessionFighter> Fighters = new();
    public SessionCameraSettings Camera = new();
    public SessionInputSettings Input = new();
}

public sealed class SessionFighter
{
    public ulong EntityId;
    /// <summary><c>player</c> or <c>cpu</c>.</summary>
    public string Role = "";
    public string Character = "";
    public string PackageId = "";
    public string PackageVersion = "";
    public string CookedContentHash = "";
    public float CapsuleHeight;
    public float CapsuleRadius;
    public string? CpuDifficulty;
    public string? CpuBehavior;
}

public sealed class SessionCameraSettings
{
    public float VerticalFovDegrees;
    public float OrbitRadius;
    public float MinPitchDegrees;
    public float MaxPitchDegrees;
    /// <summary>Largest manual right-stick yaw rate at the current sensitivity, degrees/second.</summary>
    public float StickYawRateDegrees;
    public bool LockAssist;
    public float LockYawDeadZoneDegrees;
    public float LockYawSpeedDegrees;
    /// <summary>Experiment tag for the camera handling in use.</summary>
    public string Variant = "orbit";
    /// <summary>Experiment tag for the airborne height cue in use.</summary>
    public string HeightCue = "floor-ring";
}

public sealed class SessionInputSettings
{
    public float CameraSensitivityPercent;
    public bool InvertCameraHorizontal;
    public bool InvertCameraVertical;
    public float StickDeadzonePercent;
    public string AutoLockMode = "";
}

/// <summary>One fighter's authoritative kinematics on a frame.</summary>
public struct SessionFighterFrame
{
    public ulong Id;
    public float PX, PY, PZ;
    public float VX, VY, VZ;
    public float FacingYaw;
    public bool Grounded;
    public byte State;
    public bool LockOn;
    public ulong Target;
    public ushort Damage;
    public ushort Hitstun;
}

/// <summary>
/// One simulation tick: the rendered camera, the active device, how hard the player drove the
/// camera since the previous tick, and every fighter's state after the tick.
/// </summary>
public sealed class SessionFrame
{
    public int Tick;
    public bool Gamepad;
    public float CamX, CamY, CamZ;
    public float CamQX, CamQY, CamQZ, CamQW = 1f;
    public float VerticalFov;
    public float Aspect;
    /// <summary>Camera yaw+pitch applied from the mouse since the previous tick, degrees.</summary>
    public float MouseLookDegrees;
    /// <summary>Camera yaw+pitch applied from the right stick since the previous tick, degrees.</summary>
    public float StickLookDegrees;
    /// <summary>Largest right-stick deflection (0–1) seen since the previous tick.</summary>
    public float StickPeak;
    /// <summary>Client camera mode: 0 normal orbit, 1 frozen, 2 free cursor, 3 aiming.</summary>
    public byte CameraMode;
    public SessionFighterFrame[] Fighters = Array.Empty<SessionFighterFrame>();
}

/// <summary>An accepted contact this tick, including shield blocks.</summary>
public sealed class SessionHit
{
    public int Tick;
    public ulong Attacker;
    public ulong Target;
    public byte AttackSlot;
    public ulong ActivationId;
    public bool Airborne;
    public float Damage;
    public bool Blocked;
    public float HitX, HitY, HitZ;
    public ushort HitstopTicks;
}

/// <summary>A practice-control change (position reset, CPU added, …) that splits analysis.</summary>
public sealed class SessionEvent
{
    public int Tick;
    public string Kind = "";
    public string? Detail;
}

public sealed class SessionEnd
{
    public string EndedUtc = "";
    public int Ticks;
    public string Reason = "";
}

/// <summary>Streams one session to a gzip JSON Lines destination. Frames avoid reflection.</summary>
public sealed class SessionLogWriter : IDisposable
{
    /// <summary>Ticks a swing must stay quiet after its window and last hitbox before it is final.</summary>
    public const int SwingSettleTicks = 30;

    private readonly Stream _destination;
    private readonly GZipStream _gzip;
    private readonly Utf8JsonWriter _json;
    private readonly List<int> _pendingSwings = new();
    private int _seenSwings;
    private bool _completed;

    public SessionLogWriter(Stream destination, SessionHeader header)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _gzip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: false);
        _json = new Utf8JsonWriter(_gzip, new JsonWriterOptions { SkipValidation = true });
        WriteRecord(SessionLogFormat.HeaderType, header);
    }

    public void WriteFrame(SessionFrame frame)
    {
        _json.WriteStartObject();
        _json.WriteString("type", SessionLogFormat.FrameType);
        _json.WriteStartObject("data");
        _json.WriteNumber("tick", frame.Tick);
        _json.WriteBoolean("gamepad", frame.Gamepad);
        Number("camX", frame.CamX); Number("camY", frame.CamY); Number("camZ", frame.CamZ);
        Number("camQX", frame.CamQX, 5); Number("camQY", frame.CamQY, 5);
        Number("camQZ", frame.CamQZ, 5); Number("camQW", frame.CamQW, 5);
        Number("verticalFov", frame.VerticalFov);
        Number("aspect", frame.Aspect);
        Number("mouseLookDegrees", frame.MouseLookDegrees);
        Number("stickLookDegrees", frame.StickLookDegrees);
        Number("stickPeak", frame.StickPeak);
        _json.WriteNumber("cameraMode", frame.CameraMode);
        _json.WriteStartArray("fighters");
        foreach (var fighter in frame.Fighters)
        {
            _json.WriteStartObject();
            _json.WriteNumber("id", fighter.Id);
            // Names match the camel-case serializer used by the reader ("PX" → "px").
            Number("px", fighter.PX); Number("py", fighter.PY); Number("pz", fighter.PZ);
            Number("vx", fighter.VX); Number("vy", fighter.VY); Number("vz", fighter.VZ);
            Number("facingYaw", fighter.FacingYaw, 4);
            _json.WriteBoolean("grounded", fighter.Grounded);
            _json.WriteNumber("state", fighter.State);
            _json.WriteBoolean("lockOn", fighter.LockOn);
            _json.WriteNumber("target", fighter.Target);
            _json.WriteNumber("damage", fighter.Damage);
            _json.WriteNumber("hitstun", fighter.Hitstun);
            _json.WriteEndObject();
        }
        _json.WriteEndArray();
        _json.WriteEndObject();
        _json.WriteEndObject();
        EndLine();
    }

    public void WriteHit(SessionHit hit) => WriteRecord(SessionLogFormat.HitType, hit);
    public void WriteEvent(SessionEvent sessionEvent) => WriteRecord(SessionLogFormat.EventType, sessionEvent);
    public void WriteSwing(SwingRecord swing) => WriteRecord(SessionLogFormat.SwingType, swing);

    /// <summary>
    /// Write recorder swings that can no longer change: rejected presses one tick later,
    /// accepted ones once their window and last tested hitbox are <see cref="SwingSettleTicks"/>
    /// behind <paramref name="tick"/>. <paramref name="final"/> writes everything left.
    /// </summary>
    public void WriteSettledSwings(IReadOnlyList<SwingRecord> swings, int tick, bool final = false)
    {
        for (; _seenSwings < swings.Count; _seenSwings++)
            _pendingSwings.Add(_seenSwings);
        for (int i = 0; i < _pendingSwings.Count;)
        {
            if (_pendingSwings[i] >= swings.Count) { _pendingSwings.RemoveAt(i); continue; }
            var swing = swings[_pendingSwings[i]];
            if (!final && !IsSettled(swing, tick)) { i++; continue; }
            WriteSwing(swing);
            _pendingSwings.RemoveAt(i);
        }
    }

    public static bool IsSettled(SwingRecord swing, int tick)
    {
        if (tick <= swing.StartTick) return false;
        if (!swing.Accepted) return true;
        int windowEnd = swing.StartTick + Math.Max(1, swing.WindowTicks);
        return tick >= windowEnd + SwingSettleTicks
            && (swing.LastHitboxTick < 0 || tick >= swing.LastHitboxTick + SwingSettleTicks);
    }

    /// <summary>Push buffered records to the destination so a crash loses little.</summary>
    public void Flush()
    {
        _json.Flush();
        _gzip.Flush();
        _destination.Flush();
    }

    /// <summary>Write remaining swings and the end record, then close the destination.</summary>
    public void Complete(IReadOnlyList<SwingRecord> swings, SessionEnd end)
    {
        if (_completed) return;
        WriteSettledSwings(swings, end.Ticks, final: true);
        WriteRecord(SessionLogFormat.EndType, end);
        Dispose();
    }

    public void Dispose()
    {
        if (_completed) return;
        _completed = true;
        _json.Flush();
        _json.Dispose();
        _gzip.Dispose();
    }

    private void WriteRecord<T>(string type, T value)
    {
        _json.WriteStartObject();
        _json.WriteString("type", type);
        _json.WritePropertyName("data");
        JsonSerializer.Serialize(_json, value, SessionLogFormat.JsonOptions);
        _json.WriteEndObject();
        EndLine();
    }

    private void EndLine()
    {
        _json.Flush();
        _gzip.WriteByte((byte)'\n');
        _json.Reset();
    }

    private void Number(string name, float value, int decimals = 3)
        => _json.WriteNumber(name, float.IsNaN(value) || float.IsInfinity(value)
            ? 0f : (float)Math.Round(value, decimals));
}

/// <summary>A parsed session. <see cref="Truncated"/> marks a file cut short, for example by a crash.</summary>
public sealed class SessionLogData
{
    public string Source = "";
    public SessionHeader Header = new();
    public List<SessionFrame> Frames = new();
    public List<SwingRecord> Swings = new();
    public List<SessionHit> Hits = new();
    public List<SessionEvent> Events = new();
    public SessionEnd? End;
    public bool Truncated;
}

public static class SessionLogReader
{
    public static SessionLogData ReadFile(string path)
    {
        using var file = File.OpenRead(path);
        var data = Read(file);
        data.Source = path;
        return data;
    }

    /// <summary>Read every complete record; unknown record types are skipped.</summary>
    public static SessionLogData Read(Stream stream)
    {
        var data = new SessionLogData();
        bool sawHeader = false;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip);
        try
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (!TryAdd(data, line, ref sawHeader)) { data.Truncated = true; break; }
            }
        }
        catch (Exception ex) when (ex is InvalidDataException || ex is EndOfStreamException || ex is IOException)
        {
            data.Truncated = true;
        }
        if (!sawHeader) throw new InvalidDataException("Session log has no header record.");
        if (data.End == null) data.Truncated = true;
        return data;
    }

    private static bool TryAdd(SessionLogData data, string line, ref bool sawHeader)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(line); }
        catch (JsonException) { return false; }
        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || !root.TryGetProperty("data", out var value))
                return false;
            var options = SessionLogFormat.JsonOptions;
            switch (type.GetString())
            {
                case SessionLogFormat.HeaderType:
                    data.Header = value.Deserialize<SessionHeader>(options) ?? new SessionHeader();
                    sawHeader = true;
                    if (data.Header.Schema > SessionLogFormat.SchemaVersion)
                        throw new InvalidDataException(
                            $"Session log schema {data.Header.Schema} is newer than supported {SessionLogFormat.SchemaVersion}.");
                    break;
                case SessionLogFormat.FrameType:
                    var frame = value.Deserialize<SessionFrame>(options);
                    if (frame != null) data.Frames.Add(frame);
                    break;
                case SessionLogFormat.SwingType:
                    var swing = value.Deserialize<SwingRecord>(options);
                    if (swing != null) data.Swings.Add(swing);
                    break;
                case SessionLogFormat.HitType:
                    var hit = value.Deserialize<SessionHit>(options);
                    if (hit != null) data.Hits.Add(hit);
                    break;
                case SessionLogFormat.EventType:
                    var sessionEvent = value.Deserialize<SessionEvent>(options);
                    if (sessionEvent != null) data.Events.Add(sessionEvent);
                    break;
                case SessionLogFormat.EndType:
                    data.End = value.Deserialize<SessionEnd>(options);
                    break;
            }
        }
        return true;
    }
}

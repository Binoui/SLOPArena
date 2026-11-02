using System.Text.Json;
using System.Text.Json.Serialization;
using SlopArena.Shared;

namespace SlopArena.ComboFinder;

sealed record ComboFile(int Version, string Id, string Stage, ContentStamp Content, SearchStamp Search,
    FighterTape[] Fighters, Timeline Timeline, string Summary);
sealed record ContentStamp(int Protocol, string StageHash, Dictionary<string, string> Packages);
sealed record SearchStamp(int Seed, int Beam, string Difficulty, double Score, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string[]? Route);
sealed record FighterTape(int Id, string Role, string Package, float[] Feet, float Yaw, ushort Damage, string Tape);
sealed record Timeline(int Ticks, int Ko, StringWindow String, LandedMove[] Moves, VictimSwing[] VictimSwings, int Taken, RawHit[] Hits);
/// <summary>One hit exactly as photo mode logs it, so a recording can be compared hit for hit.</summary>
sealed record RawHit(int Tick, int Owner, int Slot, float Damage, bool Blocked);
sealed record StartLayout(string Name, float[] Attacker, float[] Victim);
sealed record StringWindow(int From, int To);
sealed record LandedMove(int Tick, int LastTick, string Label, bool Air, float Damage, bool Juggle);
sealed record VictimSwing(int Tick, string Label);
sealed record CameraPlan(string Combo, string Orientation, int Width, int Height, EditWindow Edit, CameraAngle[] Angles);
sealed record EditWindow(int From, int Cut, int End, int Ko);
sealed record CameraAngle(string Name, int From, int To, PhotoCamera Camera);
sealed record PhotoCamera(float[] Pos, float[] Look, object Follow, float[] LookOffset, bool Move,
    float Ease, float Fov, float Far = 4000, float? Spread = null);
sealed record Hit(int Tick, ulong Owner, byte Slot, float Damage, bool Blocked, int Move);
sealed record MoveInstance(int tick, byte slot, bool ground);
sealed record SwingInstance(int tick, byte slot);
sealed record Position(float x, float y, float z);
sealed record Death(int Tick, ulong Entity);
sealed record RunTrace
{
    public List<Hit> Hits = new();
    public List<SwingInstance> VictimSwings = new();
    public int VictimKo = -1, AttackerKo = -1, Ticks;
    public List<InputState> TapeA = new(), TapeV = new();
    public int CloseVictimSwings;
    public List<MoveInstance> Moves = new();
    public List<bool> VictimFree = new(), AttackerAir = new(), VictimAir = new();
    public List<int> Beelines = new();
    public List<Position> AttackerPos = new(), VictimPos = new();
    public List<Death> Deaths = new();
    public Dictionary<int, float> ThrowEdgeDistances = new();
}
sealed record SearchOptions(string Attacker, string Victim, string Stage, ushort Damage, int Seed = 1,
    int Beam = 30, int Max = 660, int Patience = 25, int Seg = 6, string[]? Route = null,
    float[]? AttackerPosition = null, float[]? VictimPosition = null, string Layout = "spawn");
static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";
    public static ComboFile Read(string path) => JsonSerializer.Deserialize<ComboFile>(File.ReadAllText(path), Options)
        ?? throw new ArgumentException("Empty combo file");
}

using System.Text;
using SlopArena.Shared;
using SlopArena.Shared.AI;

public sealed class Fighter
{
    public ulong Id;
    public MatchContentEntry Entry = null!;
    public bool Cpu;
    public BotMemory Memory = new();
    public HeuristicBotPolicy Policy = new();
    public Random Rng = new(1);
    public ulong Target;
    public byte LastDeaths;
}

// One playable format. Each mode owns its rule, roster placement, per-tick input shaping and HUD data,
// so the bridge never branches on which format is running.
public abstract class MatchMode
{
    public ServerSimulation Sim = null!;
    public ArenaDefinition Arena;
    public readonly List<Fighter> Fighters = new();

    public abstract IMatchRule Rule { get; }
    public abstract string? Begin(string[] packages, MatchContentCatalog catalog, int difficulty, int seed);
    public abstract InputState HumanInput(Fighter f, InputState input);
    public virtual InputState CpuInput(Fighter f, uint tick) => default;
    public virtual void AfterTick() { }
    public abstract bool Ended { get; }
    // Static facts the page needs once, such as the Slopball net.
    public virtual void WriteCourt(StringBuilder sb) => sb.Append("{}");
    // Per-tick facts beyond the fighters, such as the ball and the combo.
    public virtual void WriteFrame(StringBuilder sb) => sb.Append("null");
    public virtual bool TryGetPoint(ulong entityId, out float x, out float y, out float z)
    {
        x = y = z = 0;
        return false;
    }

    protected Fighter Register(ulong id, MatchContentEntry entry, SpawnPoint spawn, bool cpu, int seed)
    {
        var f = new Fighter { Id = id, Entry = entry, Cpu = cpu, Rng = new Random(seed) };
        var state = new CharacterState
        {
            EntityId = id,
            PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, AimYaw = spawn.Yaw,
            JumpsLeft = entry.Definition.Movement.MaxJumps, AirDodgesLeft = 1, MatchState = MatchState.Playing,
        };
        Sim.RegisterEntity(id, entry.Definition, state, entry.BakedAnimation);
        Sim.SetRespawnPosition(id, spawn.X, spawn.Y, spawn.Z, spawn.Yaw);
        Fighters.Add(f);
        return f;
    }
}

// Stock fight on a PvP stage: P1 against one to three CPUs.
public sealed class BrawlMode : MatchMode
{
    readonly StockMatchRule _rule;
    public BrawlMode(int stocks) => _rule = new StockMatchRule((byte)stocks);
    public override IMatchRule Rule => _rule;
    public override bool Ended => _rule.Evaluate(Sim.GetAllStates()).IsEnded;

    public override string? Begin(string[] packages, MatchContentCatalog catalog, int difficulty, int seed)
    {
        if (Arena.Purpose != ArenaPurpose.PvP) return "Brawl needs a PvP stage";
        for (int i = 0; i < packages.Length; i++)
        {
            var entry = catalog.ResolvePackage(packages[i]);
            if (entry == null) return "missing package " + packages[i];
            var f = Register(i == 0 ? 1UL : 100UL + (ulong)i, entry, Arena.SpawnPoints[i % Arena.SpawnPoints.Length],
                cpu: i > 0, unchecked(seed + i * 7919));
            f.Memory.Difficulty = BotDifficultyProfile.Normalize((CpuDifficulty)difficulty);
        }
        return null;
    }

    public override InputState HumanInput(Fighter f, InputState input)
    {
        if (input.TargetEntityId == 0) input.TargetEntityId = (byte)Nearest(f.Id);
        return input;
    }

    public override InputState CpuInput(Fighter f, uint tick)
    {
        var self = Sim.GetState(f.Id);
        if (f.Target == 0 || Sim.GetState(f.Target).Deaths >= _rule.MaxStocks || tick % 120 == 0)
        {
            ulong t = Nearest(f.Id);
            if (t != f.Target) f.Memory.Reset();
            f.Target = t;
        }
        if (f.Target == 0) return default;
        f.Policy.ObserveOpponent(f.Memory, Sim.GetState(f.Target), Sim.GetDefinition(f.Target), activeMove: Sim.GetActiveMove(f.Target)?.Slot);
        var cpu = f.Policy.Decide(self, f.Entry.Definition.ForForm(self.Form.Active), f.Rng, f.Memory, Arena,
            f.Entry.BakedAnimation, activeMove: Sim.GetActiveMove(f.Id)?.Slot, canSwapCancel: Sim.CanSwapCancel(f.Id));
        cpu.TargetEntityId = (byte)f.Target;
        return cpu;
    }

    public override void AfterTick()
    {
        foreach (var f in Fighters)
        {
            var s = Sim.GetState(f.Id);
            if (s.Deaths != f.LastDeaths) { f.LastDeaths = s.Deaths; f.Memory.Reset(); f.Target = 0; }
            if (!f.Cpu) continue;
            foreach (var hit in Sim.LastTickHits)
            {
                if (hit.OwnerEntityId != f.Id || hit.TargetEntityId != f.Target) continue;
                f.Memory.RecordOpponentContact(hit.AttackSlot, hit.Blocked ? CpuContactKind.Block : CpuContactKind.Hit,
                    Sim.GetState(hit.TargetEntityId), hit.HitstopTicks);
            }
        }
    }

    ulong Nearest(ulong self)
    {
        var s = Sim.GetState(self);
        ulong best = 0;
        float bestD = float.MaxValue;
        foreach (var f in Fighters)
        {
            if (f.Id == self) continue;
            var o = Sim.GetState(f.Id);
            if (o.Deaths >= _rule.MaxStocks) continue;
            float dx = o.PX - s.PX, dz = o.PZ - s.PZ, d = dx * dx + dz * dz + (o.PY < Arena.KillHeight + 5 ? 1600 : 0);
            if (d < bestD) { bestD = d; best = f.Id; }
        }
        return best;
    }
}

// Solo Slopball: return the Launcher's serves over the net. Endless; the combo and best streak are the score.
public sealed class SlopballMode : MatchMode
{
    // Unity targets the ball only when it is within 20 m of the fighter.
    const float TargetRangeSq = 400f;
    public override IMatchRule Rule => NoWinMatchRule.Instance;
    public override bool Ended => Sim.Slopball is { Ended: true };

    public override string? Begin(string[] packages, MatchContentCatalog catalog, int difficulty, int seed)
    {
        if (Arena.Arcade?.Slopball == null || Arena.SpawnPoints.Length != 1) return "Slopball needs the Slopball court";
        var entry = catalog.ResolvePackage(packages[0]);
        if (entry == null) return "missing package " + packages[0];
        if (!Arena.Arcade.AllowsPackage(entry.Identity.PackageId)) return entry.DisplayName + " is not admitted on this court";
        Register(1UL, entry, Arena.SpawnPoints[0], cpu: false, seed);
        Sim.BeginSlopballAttempt(ArcadeRecordCompatibility.SlopballCountdownTicks, seed == 0 ? 1u : (uint)seed);
        return null;
    }

    public override InputState HumanInput(Fighter f, InputState input)
    {
        var s = Sim.GetState(f.Id);
        if (Sim.ArcadeBall is { Active: true } ball)
        {
            float dx = ball.Position.X - s.PX, dz = ball.Position.Z - s.PZ;
            if (dx * dx + dz * dz <= TargetRangeSq) input.TargetEntityId = ServerSimulation.ArcadeBallTargetInputId;
        }
        return input;
    }

    public override bool TryGetPoint(ulong entityId, out float x, out float y, out float z)
    {
        if (Sim.ArcadeBall is { } ball && ball.EntityId == entityId)
        {
            (x, y, z) = (ball.Position.X, ball.Position.Y, ball.Position.Z);
            return true;
        }
        return base.TryGetPoint(entityId, out x, out y, out z);
    }

    public override void WriteCourt(StringBuilder sb)
    {
        var c = Arena.Arcade!.Slopball!;
        sb.Append("{\"netZ\":").Append(Demo.F(c.NetZ)).Append(",\"netTop\":").Append(Demo.F(c.NetTop))
          .Append(",\"netHalf\":").Append(Demo.F(c.NetHalfThickness)).Append(",\"floorY\":").Append(Demo.F(c.FloorY))
          .Append(",\"landing\":[").Append(Demo.F(c.LandingHalfWidth)).Append(',').Append(Demo.F(c.LandingMinZ))
          .Append(',').Append(Demo.F(c.LandingMaxZ)).Append("]}");
    }

    public override void WriteFrame(StringBuilder sb)
    {
        var s = Sim.Slopball!.Value;
        var b = Sim.ArcadeBall!.Value;
        sb.Append("{\"phase\":").Append(Demo.Str(s.Phase.ToString()))
          .Append(",\"returns\":").Append(s.Returns).Append(",\"best\":").Append(s.BestStreak)
          .Append(",\"heat\":").Append(s.Heat).Append(",\"countdown\":").Append(s.CountdownTicks)
          .Append(",\"serve\":").Append(Demo.Str(s.Shot.Kind.ToString()))
          .Append(",\"launcher\":[").Append(Demo.F(s.Shot.Spawn.X)).Append(',').Append(Demo.F(s.Shot.Spawn.Y))
          .Append(',').Append(Demo.F(s.Shot.Spawn.Z)).Append(']')
          .Append(",\"ball\":{\"id\":").Append(b.EntityId).Append(",\"active\":").Append(b.Active ? "true" : "false")
          .Append(",\"p\":[").Append(Demo.F(b.Position.X)).Append(',').Append(Demo.F(b.Position.Y)).Append(',')
          .Append(Demo.F(b.Position.Z)).Append("],\"r\":").Append(Demo.F(b.Radius))
          .Append(",\"dmg\":").Append(Demo.F(b.DamagePercent)).Append(",\"stop\":").Append(b.HitstopTicks).Append('}')
          .Append(",\"events\":[");
        bool first = true;
        foreach (var e in Sim.LastTickSlopballEvents)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("[").Append(Demo.Str(e.Kind.ToString())).Append(',').Append(e.Returns).Append(']');
        }
        sb.Append("]}");
    }
}

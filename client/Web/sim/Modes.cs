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
    public virtual void BeforeTick() { }
    public virtual void AfterTick() { }
    // Page commands outside the input stream, such as the tutorial's pause-menu skip.
    public virtual void Command(string name) { }
    // Degrees the page camera turned since the last tick: presentation input, never simulated.
    public float CameraTurn;
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

// Fighter's License: Unity's TutorialMatch on the license_test deck. Shared's TutorialProgress owns every
// lesson rule and only observes the tick; the dummy is an ordinary fighter fed TutorialDummy input.
public sealed class TutorialMode : MatchMode
{
    const ulong PlayerId = 1, DummyId = 100;
    // Pause between a passed lesson and the next one, so the stamp reads.
    const int LessonGapTicks = 90;
    static TutorialStageLayout Layout => TutorialCurriculum.LicenseTest;
    static readonly byte[] Slots =
        { AbilitySlots.Normal1, AbilitySlots.Normal2, AbilitySlots.Normal3, AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F };
    TutorialProgress _progress = null!;
    MatchContentEntry _player = null!, _dummy = null!;
    CharacterState _playerBefore, _dummyBefore;
    int _nextLessonTicks = -1, _completed;
    bool _passed, _finished;

    public override IMatchRule Rule => NoWinMatchRule.Instance;
    public override bool Ended => _finished;

    // packages: the player, then the dummy (the page picks it as TutorialMatch does).
    public override string? Begin(string[] packages, MatchContentCatalog catalog, int difficulty, int seed)
    {
        if (Arena.SpawnPoints is not { Length: >= 2 }) return "the tutorial needs a stage with two spawns";
        var player = catalog.ResolvePackage(packages[0]);
        if (player == null) return "missing package " + packages[0];
        _player = player;
        _dummy = (packages.Length > 1 ? catalog.ResolvePackage(packages[1]) : null) ?? player;
        Fighters.Add(new Fighter { Id = PlayerId, Entry = _player });
        Fighters.Add(new Fighter { Id = DummyId, Entry = _dummy, Cpu = true });
        Place(PlayerId, _player, Arena.SpawnPoints[0], 0, 0);
        Place(DummyId, _dummy, Arena.SpawnPoints[1], 0, 0);
        _progress = new TutorialProgress(Layout, _player.Definition.CapsuleHeight);
        BeginLesson();
        return null;
    }

    public override void BeforeTick()
    {
        _playerBefore = Sim.GetState(PlayerId);
        _dummyBefore = Sim.GetState(DummyId);
    }

    public override InputState HumanInput(Fighter f, InputState input)
    {
        if (_finished) return default;
        if (input.TargetEntityId == 0) input.TargetEntityId = (byte)DummyId;
        return input;
    }

    public override InputState CpuInput(Fighter f, uint tick)
        => f.Id == DummyId ? TutorialDummy.Build(_progress.DummyBehavior, _dummyBefore, _playerBefore, tick) : default;

    public override void AfterTick()
    {
        _completed = _progress.Observe(new TutorialFrame(_playerBefore, Sim.GetState(PlayerId), _dummyBefore,
            Sim.GetState(DummyId), Sim.LastTickHits, CameraTurn));
        CameraTurn = 0;
        _passed = false;
        if (_progress.LessonComplete && _nextLessonTicks < 0 && !_finished)
        {
            _nextLessonTicks = LessonGapTicks;
            _passed = true;
        }
        else if (_nextLessonTicks > 0 && --_nextLessonTicks == 0)
        {
            _nextLessonTicks = -1;
            if (_progress.StartNextLesson()) BeginLesson();
            else _finished = true;
        }
    }

    public override void Command(string name)
    {
        if (_finished) return;
        if (name == "skip" && !_progress.LessonComplete)
        {
            _progress.SkipLesson();
            _nextLessonTicks = 1;
        }
        else if (name == "restart")
        {
            _nextLessonTicks = -1;
            _progress.RestartLesson();
            BeginLesson();
        }
    }

    // Lesson setup: fresh cooldowns (or none, for practice), and the dummy placed as the lesson needs.
    void BeginLesson()
    {
        var lesson = _progress.Lesson;
        byte deaths = Sim.GetState(DummyId).Deaths;
        if (lesson.DummySetup == TutorialDummySetup.Reset)
            Place(DummyId, _dummy, Arena.SpawnPoints[1], lesson.DummyPercent, deaths);
        else if (lesson.DummySetup == TutorialDummySetup.KnockoutReady)
            Place(DummyId, _dummy, new SpawnPoint { X = Layout.KnockoutX, Y = Layout.FloorHeight, Z = Layout.KnockoutZ, Yaw = Layout.KnockoutYaw },
                lesson.DummyPercent, deaths);
        Sim.NoCooldownsEntityId = lesson.NoCooldowns ? PlayerId : null;
        var player = Sim.GetState(PlayerId);
        for (byte slot = 1; slot <= AbilitySlots.Count; slot++) player.SetCooldown(slot, 0);
        Sim.SetState(PlayerId, player);
    }

    // Register a fighter fresh at a spot (floor-snapped), keeping its life count.
    void Place(ulong id, MatchContentEntry entry, SpawnPoint spot, ushort percent, byte deaths)
    {
        var def = entry.Definition;
        float y = spot.Y <= Layout.FloorHeight + 0.01f ? Layout.FloorHeight + def.CapsuleHeight * 0.5f : spot.Y;
        Sim.RemoveEntity(id);
        Sim.RegisterEntity(id, def, new CharacterState
        {
            PX = spot.X, PY = y, PZ = spot.Z, FacingYaw = spot.Yaw, AimYaw = spot.Yaw, IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1, DamagePercent = percent, Deaths = deaths,
            MatchState = MatchState.Playing,
        }, entry.BakedAnimation);
        if (id == DummyId)
        {
            var home = Arena.SpawnPoints[1];
            Sim.SetRespawnPosition(id, home.X, home.Y, home.Z, home.Yaw);
        }
        else Sim.SetRespawnPosition(id, spot.X, y, spot.Z, spot.Yaw);
    }

    // The checklist and the player's move names; the page owns the wording, as TutorialView does in Unity.
    public override void WriteCourt(StringBuilder sb)
    {
        var def = _player.Definition;
        sb.Append("{\"tallBlock\":").Append(Demo.F(Layout.TallBlockHeight)).Append(",\"lessons\":[");
        for (int i = 0; i < _progress.Lessons.Count; i++)
        {
            var lesson = _progress.Lessons[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(Demo.Str(lesson.Id.ToString())).Append(",\"objectives\":[");
            for (int o = 0; o < lesson.Objectives.Count; o++)
            {
                if (o > 0) sb.Append(',');
                sb.Append('[').Append(Demo.Str(lesson.Objectives[o].Id.ToString())).Append(',').Append(Demo.F(lesson.Objectives[o].Goal)).Append(']');
            }
            sb.Append("]}");
        }
        sb.Append("],\"moves\":{");
        for (int i = 0; i < Slots.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(Slots[i]).Append("\":[").Append(Demo.Str(MoveName(def, Slots[i], false))).Append(',')
              .Append(Demo.Str(MoveName(def, Slots[i], true))).Append(']');
        }
        sb.Append("}}");
    }

    public override void WriteFrame(StringBuilder sb)
    {
        sb.Append("{\"lesson\":").Append(_progress.LessonIndex).Append(",\"amounts\":[");
        for (int i = 0; i < _progress.Lesson.Objectives.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Demo.F(_progress.Amount(i)));
        }
        sb.Append("],\"completed\":").Append(_completed).Append(",\"passed\":").Append(_passed ? "true" : "false")
          .Append(",\"between\":").Append(_nextLessonTicks >= 0 ? "true" : "false")
          .Append(",\"license\":").Append(_progress.Finished ? "true" : "false").Append('}');
    }

    static string MoveName(CharacterDefinition def, byte slot, bool airborne)
    {
        var spec = def.GetCookedSlotAbility(slot, airborne) ?? def.GetCookedSlotAbility(slot, !airborne);
        return spec != null && !string.IsNullOrWhiteSpace(spec.Name) ? spec.Name : "that move";
    }
}

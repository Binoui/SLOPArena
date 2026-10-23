using SlopArena.Shared;
using System.Text.Json;
if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/BonkInvestigation -- [output-directory]");
    return 2;
}
var outputDirectory = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(AppContext.BaseDirectory, "report"));
Directory.CreateDirectory(outputDirectory);
var summary = new List<string>();
void Log(string message) { summary.Add(message); Console.WriteLine(message); }
var identities = new[] { CharacterClass.Bonk, CharacterClass.FightGuy, CharacterClass.Wibou }
    .Select(character => { var entry = BuiltInContentResolver.Resolve(character); return new { character = character.ToString(), entry.Identity }; }).ToArray();
File.WriteAllText(Path.Combine(outputDirectory, "content-identities.json"), JsonSerializer.Serialize(identities, new JsonSerializerOptions { WriteIndented = true }));
var arena = new ArenaDefinition { Name = "probe", KillHeight = -1000, SpawnPoints = new[] { new SpawnPoint() }, Heightmap = new ArenaHeightmap { Data = new float[200 * 200], Width = 200, Height = 200, CellSize = 1 } };
var victim = BuiltInContentResolver.Resolve(CharacterClass.FightGuy);
var rows = new List<object>();
// Real contact trace: target one metre ahead, neutral inputs after the attack.
foreach (var who in new[] { CharacterClass.Bonk, CharacterClass.Wibou })
    foreach (int slot in new[] { 1, 2, 3, 4 })
    {
        var entry = BuiltInContentResolver.Resolve(who); var def = entry.Definition;
        byte inputSlot = (byte)(slot switch { 1 => 3, 2 => 7, 3 => 8, _ => 9 });
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState { PX = 100, PZ = 100, PY = def.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1 }, entry.BakedAnimation);
        sim.RegisterEntity(100, victim.Definition, new CharacterState { PX = 100, PZ = 101, PY = victim.Definition.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = victim.Definition.Movement.MaxJumps, AirDodgesLeft = 1, FacingYaw = MathF.PI }, victim.BakedAnimation);
        var inputs = new Dictionary<ulong, InputState> { { 1, default }, { 100, default } };
        var frames = new List<object>(); int hitTick = -1;
        for (int tick = 0; tick < 180; tick++)
        {
            inputs[1] = tick == 0 ? new InputState { ActiveSlot = inputSlot } : default; sim.Tick(inputs);
            var s = sim.GetState(1); var v = sim.GetState(100);
            foreach (var h in sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == 1)) frames.Add(new { tick, elapsed = s.AttackElapsedTicks, facing = s.FacingYaw, feet = s.PY - def.CapsuleHeight / 2, start = new[] { h.X - s.PX, h.Y, h.Z - s.PZ }, end = new[] { h.EndX - s.PX, h.EndY, h.EndZ - s.PZ }, radius = h.Radius, age = h.AgeTicks, anchor = h.SourceEvent.BoneName, endAnchor = h.SourceEvent.EndBoneName, victim = new[] { v.PX - s.PX, v.PY, v.PZ - s.PZ } });
            if (hitTick < 0 && sim.LastTickHits.Any(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100 && h.Damage > 0)) hitTick = tick;
        }
        rows.Add(new { who = who.ToString(), slot, hitTick, frames });
        Log($"{who} g{slot}: hit={hitTick}, activeFrames={frames.Count}");
    }
File.WriteAllText(Path.Combine(outputDirectory, "geometry.json"), JsonSerializer.Serialize(rows));
// Full pose sweep: geometry only, including ticks outside the damaging window.
var trajectories = new List<object>();
var bonk = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
foreach (bool air in new[] { false, true })
    foreach (int n in new[] { 1, 2, 3, 4 })
    {
        byte inputSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[n - 1]);
        var spec = bonk.Definition.GetCookedSlotAbility(inputSlot, air)!;
        var stage = spec.Timeline.Stages[0];
        var frames = new List<object>();
        if (stage.ReadHitboxes() != null) foreach (var evt in stage.ReadHitboxes())
            {
                for (ushort t = 0; t < stage.DurationTicks; t++)
                {
                    var state = new CharacterState { PY = bonk.Definition.CapsuleHeight / 2, AttackElapsedTicks = t };
                    HitboxGeometry.ResolvePositions(state, evt, bonk.BakedAnimation, bonk.Definition, bonk.Definition?.GetCookedSlotAbility(inputSlot, air), 0, out float x, out float y, out float z, out float ex, out float ey, out float ez);
                    frames.Add(new { t, start = new[] { x, y, z }, end = new[] { ex, ey, ez }, radius = evt.Radius });
                }
            }
        trajectories.Add(new { air, n, duration = stage.DurationTicks, iasa = stage.IasaTicks, animation = spec.Timeline.Stages.SelectMany(x => x.AnimationIds), frames });
    }
File.WriteAllText(Path.Combine(outputDirectory, "trajectories.json"), JsonSerializer.Serialize(trajectories));
// Jump-relative input ticks; -1 is the no-attack control. No victim in these runs.
foreach (bool shortHop in new[] { false, true })
    foreach (int press in new[] { -1, 7, 15, 25 })
        foreach (int n in (press < 0 ? new[] { 0 } : new[] { 1, 3, 4 }))
        {
            var def = bonk.Definition; var sim = new ServerSimulation(arena);
            sim.RegisterEntity(1, def, new CharacterState { PX = 100, PZ = 100, PY = def.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = def.Movement.MaxJumps, AirDodgesLeft = 1 }, bonk.BakedAnimation);
            var inputs = new Dictionary<ulong, InputState>(); bool tookOff = false; int landing = -1, firstBox = -1, lag = -1, activation = -1; float apex = 0;
            for (int t = 0; t < 160; t++)
            {
                inputs[1] = new InputState { Jump = t == 0, JumpHeld = !shortHop && t < 40, ActiveSlot = t == press ? (byte)(n switch { 1 => 3, 3 => 8, _ => 9 }) : (byte)0 };
                sim.Tick(inputs); var s = sim.GetState(1); apex = Math.Max(apex, s.PY - def.CapsuleHeight / 2);
                if (!s.IsGrounded) tookOff = true;
                if (activation < 0 && s.State == ActionState.Attacking) activation = t;
                if (firstBox < 0 && sim.Resolver.GetActiveHitboxes().Count > 0) firstBox = t;
                if (tookOff && s.IsGrounded) { landing = t; lag = s.LandingLagTicks; break; }
            }
            Log($"JUMP short={shortHop} press={press} a{n} activation={activation} firstBox={firstBox} land={landing} lag={lag} apex={apex:F2}");
        }
// Diagnostic counterfactual: shift all bones in an isolated pose copy, not disk assets.
// This changes attacker hurtboxes too; it is not a proposed gameplay correction.
foreach (int n in new[] { 1, 2, 3, 4 })
    foreach (int phase in new[] { 0, 8, 16 })
    {
        var entry = BuiltInContentResolver.Resolve(CharacterClass.Bonk);
        byte inputSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[n - 1]);
        var spec = entry.Definition.GetCookedSlotAbility(inputSlot, false)
            ?? throw new InvalidDataException($"Bonk ground.{n} is missing.");
        if (spec.ClipCount == 0)
            throw new InvalidDataException($"Bonk ground.{n} has no animation binding.");
        var baked = entry.BakedAnimation ?? throw new InvalidDataException("Bonk has no cooked poses.");
        int animationIndex = baked.FindAnimIndex(spec.Clip(0).AnimationId);
        if (animationIndex < 0) throw new InvalidDataException($"Missing pose '{spec.Clip(0).AnimationId}'.");
        var anim = baked.Animations[animationIndex];
        var original = anim.Frames;
        baked = new BakedAnimationData
        {
            BoneNames = baked.BoneNames,
            Animations = baked.Animations.Select((animation, index) => index == animationIndex
                ? new BakedAnimationData.BakedAnim { Name = animation.Name, Frames = Enumerable.Range(0, original.Count)
                    .Select(i => original[Math.Min(i + phase, original.Count - 1)]).ToArray() }
                : animation).ToArray(),
        };
        int hits = 0; var positions = new List<string>();
        foreach (float x in new[] { -1f, -.5f, 0f, .5f, 1f })
            foreach (float z in new[] { .5f, 1f, 1.5f, 2f, 2.5f, 3f })
            {
                var sim = new ServerSimulation(arena); var def = entry.Definition;
                sim.RegisterEntity(1, def, new CharacterState { PX = 100, PZ = 100, PY = def.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1 }, baked);
                sim.RegisterEntity(100, victim.Definition, new CharacterState { PX = 100 + x, PZ = 100 + z, PY = victim.Definition.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1 }, victim.BakedAnimation);
                var inputs = new Dictionary<ulong, InputState> { { 100, default } };
                for (int t = 0; t < 100; t++)
                {
                    inputs[1] = new InputState { ActiveSlot = t == 0 ? inputSlot : (byte)0 }; sim.Tick(inputs);
                    if (sim.LastTickHits.Any(h => h.OwnerEntityId == 1 && h.TargetEntityId == 100 && h.Damage > 0)) { hits++; positions.Add($"{x},{z}@{t}"); break; }
                }
            }
        Log($"PHASE g{n} shift={phase} hits={hits}/30 positions={string.Join(';', positions)}");
    }
// Probe current grounded movement/jump recovery after a whiff. The historical
// Dash wire bit is ignored; air dodge is an airborne defense action, not a
// grounded recovery option.
foreach (int n in new[] { 1, 2, 3, 4 })
    foreach (string action in new[] { "move", "jump" })
    {
        var def = bonk.Definition; byte slot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[n - 1]);
        var sim = new ServerSimulation(arena);
        sim.RegisterEntity(1, def, new CharacterState { PX = 100, PZ = 100, PY = def.CapsuleHeight / 2, IsGrounded = true, State = ActionState.Idle, JumpsLeft = 2, AirDodgesLeft = 1 }, bonk.BakedAnimation);
        var input = new Dictionary<ulong, InputState>(); int unlocked = -1;
        for (int t = 0; t < 130; t++)
        {
            input[1] = new InputState { ActiveSlot = t == 0 ? slot : (byte)0, MoveY = t > 0 && action == "move" ? 1f : 0f, Jump = t > 0 && action == "jump", JumpHeld = action == "jump" };
            sim.Tick(input); var s = sim.GetState(1);
            if (t > 0 && (action == "move" ? Math.Abs(s.PZ - 100) > .001 : s.State == ActionState.JumpSquat)) { unlocked = t; break; }
        }
        Log($"RECOVERY g{n} {action} firstTick={unlocked}");
    }

File.WriteAllLines(Path.Combine(outputDirectory, "summary.txt"), summary);
Console.WriteLine($"Wrote diagnostic evidence to {outputDirectory}");
return 0;

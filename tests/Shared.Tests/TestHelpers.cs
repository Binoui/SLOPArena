using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
namespace SlopArena.Shared.Tests;

public static class TestHelpers
{
    public static InputState ReservedInput(byte bits, InputState input = default)
    {
        Span<byte> bytes = stackalloc byte[InputState.Size];
        input.Write(bytes);
        bytes[8] |= bits;
        return InputState.Deserialize(bytes);
    }

    public static CharacterDefinition WithMove(CharacterDefinition definition, byte wireSlot, bool airborne,
        string name, ushort duration, ushort iasa = 0, HitboxEvent[]? hits = null,
        string[]? animations = null, string? hitPresentationId = null)
    {
        CanonicalSlotProjection.TryGet(wireSlot, airborne, out var address);
        var slots = definition.CookedSlots!.ToArray();
        var operations = (hits ?? Array.Empty<HitboxEvent>()).Select(hit =>
        {
            var kb = hit.Knockback.Resolve();
            return (CookedTimelineOperation)new CookedSpawnHitboxOperation(hit.TriggerTick, AuthoringUnit.Meters,
                new CookedHitbox((AuthoringHitboxShape)hit.Shape, hit.Radius, hit.OffX, hit.OffY, hit.OffZ,
                    hit.EndOffX, hit.EndOffY, hit.EndOffZ, hit.BoneName, hit.EndBoneName, hit.Damage,
                    kb.angle, kb.baseKB, kb.growthKB, hit.StunTicks, hit.DurationTicks, hit.Interruptible,
                    hit.HitGroup, hit.KnockbackDirection, hit.FixedHitstunTicks));
        }).ToArray();
        slots[address.Ordinal] = new CookedSlotDefinition(address.Ordinal, address.Id, airborne, name, "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None, 0, false, false,
            new CookedTimeline(new[] { new CookedStage(duration, iasa, 0, 0, 0,
                animations ?? Array.Empty<string>(), operations) }), hitPresentationId: hitPresentationId);
        return definition with { CookedSlots = slots };
    }

    /// <summary>Compile saved authoring for lifecycle checks before asset cooking, optionally edited first.</summary>
    public static CharacterDefinition ResolveSourceDef(string packageId,
        Action<System.Text.Json.Nodes.JsonObject>? edit = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string relative = Path.Combine("client", "Unity", "Assets", "CharacterPackages", packageId);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, relative)))
            directory = directory.Parent;
        Assert.NotNull(directory);
        string source = Path.Combine(directory!.FullName, relative);
        string character = File.ReadAllText(Path.Combine(source, "character.json"));
        if (edit != null)
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(character)!.AsObject();
            edit(json);
            character = json.ToJsonString();
        }
        var compiled = CharacterPackageCompiler.Compile(
            File.ReadAllText(Path.Combine(source, "package.json")), character, CharacterCookProfile.TrustedBuiltIn);
        Assert.True(compiled.CookedPackage != null,
            string.Join("; ", compiled.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage!);
    }

    public static CharacterDefinition MankiDef => BuiltInContentResolver.Resolve(CharacterClass.Manki).Definition;

    public static CharacterDefinition FightGuyDef => BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;

    public static CharacterDefinition WibouDef => BuiltInContentResolver.Resolve(CharacterClass.Wibou).Definition;

    public static CharacterDefinition ResolveDef(CharacterClass selector)
        => BuiltInContentResolver.Resolve(selector).Definition;

    /// <summary>Fresh engine-only data. Roster tuning must not change mechanics fixtures.</summary>
    public static CharacterDefinition EngineDef
    {
        get
        {
            var suffixes = new[] { "1", "2", "3", "4", "A", "E", "R", "F" };
            var slots = new CookedSlotDefinition[16];
            for (var ordinal = 0; ordinal < slots.Length; ordinal++)
            {
                var air = ordinal >= 8;
                slots[ordinal] = new CookedSlotDefinition(
                    ordinal, (air ? "air." : "ground.") + suffixes[ordinal % 8], air,
                    "Engine fixture", "", "", AuthoringAbilityBehavior.MeleeCombo,
                    AuthoringAimMode.None, 0, false, false,
                    new CookedTimeline(new[]
                    {
                        new CookedStage(30, 0, 0, 0, 0,
                            Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
                    }));
            }
            return new CharacterDefinition
            {
                Class = CharacterClass.Manki,
                DisplayName = "Engine fixture",
                CapsuleRadius = 0.3f,
                CapsuleHeight = 1.5f,
                HipHeight = 0.75f,
                ShieldRadius = 0.9f,
                HurtboxRadius = 0.3f,
                HurtboxCapsules = new[] { new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f) },
                CookedSlots = slots,
                HurtboxBoneScale = 1f,
                VisualScale = 1f,
                Movement = new MovementStats
                {
                    RunSpeed = 14f, RunAccelerationA = 20f, RunAccelerationB = 12f,
                    DashSpeed = 20f, AirDodgeSpeed = 11f, AirSpeedMax = 7.5f,
                    AirAccelStick = 16f, AirAccelBase = 3f,
                    JumpForce = 12f, ShortHopForce = 7.2f,
                    AirJumpVMultiplier = 0.85f, AirJumpHMultiplier = 0.8f,
                    Gravity = 36f, AirFloatGravity = 0f,
                    DashDurationTicks = 20, DashCooldownTicks = 48,
                    GroundFriction = 8f, AirFriction = 6f,
                    MaxFallSpeed = 14.2f, FastFallSpeed = 22f,
                    MaxJumps = 2, JumpSquatTicks = 4, FloatWindowTicks = 35, RushTicks = 10,
                },
            };
        }
    }

    /// <summary>
    /// Create a player state at (x, z). PY defaults to 0 — physics tests
    /// that need grounded must set PY = floorY + def.CapsuleHeight * 0.5f.
    /// </summary>
    public static CharacterState PlayerState(float x = 0f, float z = 0f)
    {
        return new CharacterState
        {
            EntityId = 1,
            PX = x, PY = 0, PZ = z,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = 2,
            AirDodgesLeft = 1,
            FacingYaw = 0,
        };
    }

    public static CharacterState NpcState(float x = 0f, float z = 0f)
    {
        var s = PlayerState(x, z);
        s.EntityId = 100;
        return s;
    }

    /// <summary>
    /// Arena with a 1x1 heightmap at floorY. Callers must adjust
    /// entity PY to (floorY + def.CapsuleHeight * 0.5f) for groundedness.
    /// </summary>
    public static ArenaDefinition TestArena(float floorY = 0f)
    {
        int w = 200, h = 200;
        var data = new float[w * h];
        Array.Fill(data, floorY);
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[]
            {
                new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 },
            },
            Heightmap = new ArenaHeightmap
            {
                Data = data,
                Width = w,
                Height = h,
                CellSize = 1f,
                OriginX = 0f,
                OriginZ = 0f,
            },
        };
    }

    /// <summary>
    /// Create a minimal input state, defaulting all fields to 0/false.
    /// </summary>
    public static InputState Input(byte activeSlot = 0, bool jump = false,
        float moveX = 0f, float moveY = 0f, bool aiming = false, ushort aimDistance = 0,
        bool jumpHeld = false, bool down = false)
    {
        return new InputState
        {
            ActiveSlot = activeSlot,
            Jump = jump,
            JumpHeld = jumpHeld,
            Down = down,
            MoveX = moveX,
            MoveY = moveY,
            IsAiming = aiming,
            AimDistance = aimDistance,
        };
    }

    /// <summary>
    /// Create a fresh simulation for the given arena.
    public static ServerSimulation MakeSim(ArenaDefinition? arena = null)
    {
        return new ServerSimulation(arena ?? TestArena());
    }

    /// <summary>
    /// Register entity 1 with the given definition and state.
    /// </summary>
    public static void RegisterPlayer(ServerSimulation sim, CharacterDefinition def, CharacterState state)
    {
        sim.RegisterEntity(1, def, state);
    }

    /// <summary>
    /// Register entity 100 (NPC) with the given definition and state.
    /// </summary>
    public static void RegisterNpc(ServerSimulation sim, CharacterDefinition def, CharacterState state)
    {
        sim.RegisterEntity(100, def, state);
    }

    /// <summary>
    /// Run N ticks, feeding firstInput on tick 0 and default input on ticks 1..N-1.
    /// Returns entity 1's state after tick N.
    /// </summary>
    public static CharacterState TickN(ServerSimulation sim, InputState firstInput, int totalTicks)
    {
        var inputs = new Dictionary<ulong, InputState> { { 1, firstInput } };
        for (int i = 0; i < totalTicks; i++)
        {
            if (i > 0) inputs[1] = default;
            sim.Tick(inputs);
        }
        return sim.GetState(1);
    }

    /// <summary>
    /// Run N ticks with all-default input. Returns entity 1's state.
    /// </summary>
    public static CharacterState TickDefault(ServerSimulation sim, int totalTicks)
    {
        return TickN(sim, default, totalTicks);
    }

    /// <summary>
    /// Run N ticks feeding the SAME input every tick (a held input — e.g. holding
    /// the jump key through JumpSquat for a full jump, or holding Down for fast fall).
    /// Returns entity 1's state after tick N.
    /// </summary>
    public static CharacterState TickHold(ServerSimulation sim, InputState heldInput, int totalTicks)
    {
        var inputs = new Dictionary<ulong, InputState> { { 1, heldInput } };
        for (int i = 0; i < totalTicks; i++)
            sim.Tick(inputs);
        return sim.GetState(1);
    }

    /// <summary>
    /// Compute the ground-level PY for a given def with the floor at floorY.
    /// </summary>
    public static float GroundPY(CharacterDefinition def, float floorY = 0f)
    {
        return floorY + def.CapsuleHeight * 0.5f;
    }

    /// <summary>
    /// Return the ground-level PY for Manki with floor at 0.
    /// </summary>
    public static float MankiGroundPY => 0f + MankiDef.CapsuleHeight * 0.5f; // 0.65


        /// <summary>
        /// Create a state near the edge of the test arena (X=200 boundary), airborne and falling.
        /// posX: edge-adjacent position. py: starting Y. vy: downward velocity.
        /// </summary>
        public static CharacterState EdgeState(float posX = 199.5f, float py = 0.65f, float vy = -5f)
        {
            var state = PlayerState(posX, 0);
            state.PY = py;
            state.VY = vy;
            state.IsGrounded = false;
            return state;
        }
    /// <summary>
    /// Approximate float equality within tolerance.
    /// Use this instead of Assert.Equal(float, float, int) which checks decimal precision.
    /// </summary>
    public static void AssertNear(float expected, float actual, float tolerance = 0.001f)
    {
        float diff = Math.Abs(expected - actual);
        Assert.True(diff <= tolerance,
            $"Expected {expected:F6} ± {tolerance:F6} but got {actual:F6} (diff={diff:F6})");
    }

    /// A fresh CharacterDefinition with Manki's cooked slots but simple capsule
    /// hurtboxes (a standing capsule from feet to head). This lets hitbox collision
    /// tests run without baked skeleton data.
    /// Returns a new instance each access — safe to mutate in tests.
    /// </summary>
    public static CharacterDefinition CombatDef
    {
        get
        {
            var src = MankiDef;
            return new CharacterDefinition
            {
                Class = src.Class,
                DisplayName = src.DisplayName,
                CapsuleRadius = src.CapsuleRadius,
                CapsuleHeight = src.CapsuleHeight,
                ShieldRadius = src.ShieldRadius,
                HurtboxRadius = src.HurtboxRadius,
                Movement = src.Movement,


















                CookedSlots = src.CookedSlots,
                ClipOverrides = src.ClipOverrides,
                // Use a simple full-body capsule instead of bone-attached hurtboxes
                HurtboxCapsules = new[] { new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f) },
                HurtboxBoneDefs = null,
                IdleAnim = src.IdleAnim,
                RunAnim = src.RunAnim,
                DashAnim = src.DashAnim,
                JumpAnim = src.JumpAnim,
                FallAnim = src.FallAnim,
                HitSmallAnim = src.HitSmallAnim,
                HitMediumAnim = src.HitMediumAnim,
                HitHardAnim = src.HitHardAnim,
                VisualScale = src.VisualScale,
                ModelYOffset = src.ModelYOffset,
                ModelSoleOffset = src.ModelSoleOffset,
            };
        }
    }

    /// <summary>
    /// Spawn PY for <see cref="CombatDef"/> with the floor at 0.
    ///
    /// This is 0.65, NOT the settled ground PY. CombatDef clones MankiDef, whose CapsuleHeight
    /// is 1.5 m, so ground resolution snaps a dummy spawned here to PY = 0.75 on its first tick
    /// — which is what every golden's NpcFinal.PY records. 0.65 is inside
    /// PlatformSnapTolerance, so the snap is silent and the spawn value is harmless; it is kept
    /// because changing it would rewrite every golden for no behavioural gain.
    /// </summary>
    public static float CombatGroundPY => 0.65f;

    /// <summary>
    /// A CharacterDefinition with Manki's cooked slots and HurtboxBoneDefs (for BoneName
    /// lookup), but no baked data path. Useful for testing bone-attached hitbox geometry.
    /// </summary>
    public static CharacterDefinition BoneHitboxTestDef
    {
        get
        {
            var src = MankiDef;
            return new CharacterDefinition
            {
                Class = src.Class,
                DisplayName = src.DisplayName,
                CapsuleRadius = src.CapsuleRadius,
                CapsuleHeight = src.CapsuleHeight,
                HurtboxRadius = src.HurtboxRadius,
                Movement = src.Movement,


















                CookedSlots = src.CookedSlots,
                ClipOverrides = src.ClipOverrides,
                // HurtboxBoneDefs for BoneName lookup
                HurtboxBoneDefs = new HurtboxBoneDef[]
                {
                    new("mixamorig:Head", 0, 0, 0, 0.25f),
                    new("mixamorig:Spine2", 0, 0, 0, 0.3f),
                    new("mixamorig:RightFoot", 0, 0, 0, 0.18f),
                    new("mixamorig:LeftFoot", 0, 0, 0, 0.18f),
                },
                // No baked data — bone hitboxes will be skipped
                BakedDataPath = "",
                // Fallback capsule for regular collision
                HurtboxCapsules = new[] { new HurtboxCapsule(0, -0.65f, 0, 0, 0.65f, 0, 0.3f) },
                IdleAnim = src.IdleAnim,
                RunAnim = src.RunAnim,
                DashAnim = src.DashAnim,
                JumpAnim = src.JumpAnim,
                FallAnim = src.FallAnim,
                HitSmallAnim = src.HitSmallAnim,
                HitMediumAnim = src.HitMediumAnim,
                HitHardAnim = src.HitHardAnim,
                VisualScale = src.VisualScale,
                ModelYOffset = src.ModelYOffset,
                ModelSoleOffset = src.ModelSoleOffset,
            };
        }
    }

    /// <summary>
    /// Create a deep copy of a CharacterDefinition, optionally overriding Movement.
    /// </summary>
    public static CharacterDefinition CloneDef(CharacterDefinition src, MovementStats? movement = null)
    {
        var mov = movement ?? src.Movement;
        return new CharacterDefinition
        {
            Class = src.Class,
            DisplayName = src.DisplayName,
            Movement = mov,
            Weight = src.Weight,
            CaptureGeometry = src.CaptureGeometry,
            CapsuleRadius = src.CapsuleRadius,
            CapsuleHeight = src.CapsuleHeight,
            ShieldRadius = src.ShieldRadius,
            HurtboxRadius = src.HurtboxRadius,
            HipHeight = src.HipHeight,


















            CookedSlots = src.CookedSlots,
            ClipOverrides = src.ClipOverrides,
            HurtboxCapsules = src.HurtboxCapsules,
            HurtboxBoneDefs = src.HurtboxBoneDefs,
            BakedDataPath = src.BakedDataPath,
            IdleAnim = src.IdleAnim,
            RunAnim = src.RunAnim,
            DashAnim = src.DashAnim,
            JumpAnim = src.JumpAnim,
            FallAnim = src.FallAnim,
            HitSmallAnim = src.HitSmallAnim,
            HitMediumAnim = src.HitMediumAnim,
            HitHardAnim = src.HitHardAnim,
            VisualScale = src.VisualScale,
            ModelYOffset = src.ModelYOffset,
            ModelSoleOffset = src.ModelSoleOffset,
            AutoModelYOffset = src.AutoModelYOffset,
            ModelResourcePath = src.ModelResourcePath,
            LandStartOffset = src.LandStartOffset,
            HurtboxBoneScale = src.HurtboxBoneScale,
        };
    }

    public static CharacterDefinition WithEmptyAirSlot2(CharacterDefinition source)
    {
        var def = CloneDef(source);
        var slots = source.CookedSlots!.ToArray();
        slots[9] = new CookedSlotDefinition(
            9, "air.2", true, "Empty aerial", "", "",
            AuthoringAbilityBehavior.MeleeCombo, AuthoringAimMode.None,
            0, false, false,
            new CookedTimeline(new[]
            {
                new CookedStage(30, 0, 0, 0, 0,
                    Array.Empty<string>(), Array.Empty<CookedTimelineOperation>()),
            }));
        def = def with { CookedSlots = slots };
        return def;
    }

    /// <summary>
    /// Load baked skeleton data from disk, mirroring MatchInstance.LoadBakedData.
    /// Resolves "res://data/..." from the test assembly to the repo root.
    /// Returns null if file not found (callers fall back to capsule hurtboxes).
    /// </summary>
    public static BakedAnimationData? LoadBakedData(CharacterDefinition def)
    {
        if (def.CookedSlots != null)
            return BuiltInContentResolver.Resolve(def.Class).BakedAnimation;
        if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
        string relative = def.BakedDataPath.Replace("res://", "");
        // Test runs from tests/Shared.Tests/bin/Debug/net8.0/
        // Repo root is 5 dirs up from AppContext.BaseDirectory
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string path = Path.Combine(repoRoot, relative);
        if (!File.Exists(path)) return null;
        return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path));
    }
    }

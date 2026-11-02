using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

/// <summary>Isolated victim flights for compiled source normals and special hit components.
/// Full move sequences, contact geometry and recovery movement are checked in Ability Lab.
/// </summary>
internal static class KnockbackAuditReport
{
    internal sealed record Component(string Detail, HitboxEvent Hit);

    internal static int Run(string[] args)
    {
        string character = args[0];
        string? output = Program.ParseArg(args, "--json");
        if (output == null) throw new ArgumentException("--knockback-audit requires --json <path>");
        string root = Program.ParseArg(args, "--source-root") ?? "client/Unity/Assets/CharacterPackages";
        string manifest = File.ReadAllText(Path.Combine(root, character, "package.json"));
        string source = File.ReadAllText(Path.Combine(root, character, "character.json"));
        var compiled = CharacterPackageCompiler.Compile(manifest, source, CharacterCookProfile.TrustedBuiltIn);
        if (compiled.HasErrors || compiled.CookedPackage == null)
            throw new InvalidDataException(string.Join("\n", compiled.Diagnostics));
        var def = CookedCharacterRuntimeAdapter.ToCharacterDefinition(compiled.CookedPackage);
        int[] pcts = Program.ParsePcts(args) ?? new[] { 0, 60, 120, 200 };
        var moves = new List<object>();
        foreach (var slot in compiled.CookedPackage.Definition.Slots)
        {
            string slotName = (slot.IsAir ? "a" : "g") + slot.Id.Split('.')[1];
            int index = 0;
            foreach (var stage in slot.Timeline.Stages)
            foreach (var operation in stage.Operations)
            foreach (var component in Components(operation))
            {
                var h = component.Hit;
                moves.Add(new
                {
                    label = $"{slotName} {slot.Name} · {component.Detail}", slot = slotName,
                    hitIndex = index++, ability = slot.Name, detail = component.Detail,
                    frame = new { damage = h.Damage, angle = h.Knockback.Angle,
                        baseKb = h.Knockback.BaseKnockback, growth = h.Knockback.KnockbackGrowth,
                        fixedHitstunTicks = h.FixedHitstunTicks, direction = h.KnockbackDirection.ToString() },
                    trajectories = pcts.Select(pct => Flight(def, h, pct)).ToArray(),
                });
            }
        }
        var report = new
        {
            character = def.DisplayName, generatedAt = DateTime.UtcNow.ToString("O"),
            metadata = new
            {
                sourceSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
                tickRateHz = 60, victimWeight = def.Weight,
                method = "Compiled source. Isolated component flights through real queued launch and fixed hitstun. No DI/SDI, contact geometry, hitstop time, multihit accumulation, self-hit hooks or follow-up inputs. Spikes start 3m above the grounded stance; later ground carry is excluded.",
            },
            percents = pcts, moves,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Console.WriteLine($"{def.DisplayName}: {moves.Count} components at {pcts.Length} percents → {output}");
        return 0;
    }

    private static HitboxEvent Hit(float damage, float angle, float baseKb, float growth, ushort stun, ushort fixedStun = 0)
        => new() { Damage = damage, StunTicks = stun, FixedHitstunTicks = fixedStun,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = (sbyte)angle,
                BaseKnockback = baseKb, KnockbackGrowth = growth } };

    private static HitboxEvent Hit(CookedHitbox h, float? damage = null)
    {
        var hit = Hit(damage ?? h.Damage, h.Angle, h.BaseKnockback, h.KnockbackGrowth, h.StunTicks, h.FixedHitstunTicks);
        hit.KnockbackDirection = h.KnockbackDirection;
        return hit;
    }

    private static HitboxEvent Hit(CookedProjectile p)
        => Hit(p.Damage, p.Angle, p.BaseKnockback, p.KnockbackGrowth, p.StunTicks);

    internal static IEnumerable<Component> Components(CookedTimelineOperation operation)
    {
        switch (operation)
        {
            case CookedSpawnHitboxOperation h:
                yield return new($"hitbox at frame {h.Tick}", Hit(h.Hitbox)); break;
            case CookedSpawnProjectileOperation p:
                yield return new($"projectile at frame {p.Tick}", Hit(p.Projectile));
                if (p.Projectile.Return?.Hit is { } back)
                    yield return new("return leg", Hit(back.Damage, back.Angle, back.BaseKnockback, back.KnockbackGrowth, back.StunTicks));
                break;
            case CookedStartCapabilityOperation capability:
                switch (capability.Parameters)
                {
                    case CookedChargedProjectileCapabilityParameters p:
                        yield return new("projectile (all charge tiers share launch)", Hit(p.Projectile)); break;
                    case CookedDashStrikeCapabilityParameters d:
                        yield return new("light cut", Hit(d.LightHitbox));
                        yield return new("endpoint tip (alternative)", Hit(d.TipHitbox));
                        break;
                    case CookedSteeredChargeCapabilityParameters charge:
                        yield return new("scoop contact", Hit(charge.LanceHitbox));
                        var fling = charge.Carry.Fling;
                        yield return new("carry fling", Hit(fling.Damage, fling.Angle, fling.BaseKnockback, fling.KnockbackGrowth, fling.StunTicks));
                        break;
                    case CookedTargetedLeapCapabilityParameters leap:
                        yield return new("landing hit", Hit(leap.Hitbox)); break;
                    case CookedFlashCutCapabilityParameters flash:
                        yield return new("held cut (launches on the click)", Hit(flash.CutHitbox)); break;
                    case CookedCycloneKickCapabilityParameters cyclone:
                        yield return new("body / side hit", Hit(cyclone.Damage, cyclone.KnockbackAngle, cyclone.KnockbackBase, cyclone.KnockbackGrowth, cyclone.StunTicks)); break;
                    case CookedMankiRoundBombCapabilityParameters bomb:
                        yield return new("explosion", Hit(bomb.ExplosionDamage, bomb.ExplosionKbAngle, bomb.ExplosionKbBase, bomb.ExplosionKbGrowth, bomb.ExplosionStunTicks)); break;
                    case CookedMankiJetpackBoostCapabilityParameters jet:
                        yield return new("ignition explosion", Hit(jet.ExplosionDamage, jet.ExplosionKbAngle, jet.ExplosionKbBase, jet.ExplosionKbGrowth, jet.ExplosionStunTicks)); break;
                    case CookedMankiBazookaCapabilityParameters rocket:
                        yield return new("direct projectile", Hit(rocket.Damage, rocket.KbAngle, 6f, 9f, rocket.StunTicks));
                        yield return new("explosion", Hit(10f, rocket.ExplosionKbAngle, rocket.ExplosionKbBase, rocket.ExplosionKbGrowth, rocket.ExplosionStunTicks)); break;
                    case CookedMankiKamikazeCapabilityParameters fire:
                        yield return new("Kamikaze blast", Hit(fire.ExplosionDamage, fire.ExplosionKbAngle, fire.ExplosionKbBase, fire.ExplosionKbGrowth, fire.ExplosionStunTicks)); break;
                    case CookedRisingDragonCapabilityParameters:
                    case CookedWibouRisingSlashCapabilityParameters:
                        break; // Movement only; ordinary timeline operations own their hitboxes.
                    default: throw new NotSupportedException($"Audit does not cover {capability.CapabilityId}");
                }
                break;
        }
    }

    internal static object Flight(CharacterDefinition def, HitboxEvent hit, int pct)
    {
        var arena = Program.BuildArena();
        // Leave floor behind the origin too: cos(90°) is a tiny negative float,
        // which otherwise sends an exact vertical launch outside the heightmap.
        arena.Heightmap.OriginX = arena.Heightmap.OriginZ = -10f;
        var sim = new ServerSimulation(arena);
        float ground = def.CapsuleHeight * .5f;
        var kb = hit.Knockback;
        bool spike = kb.Angle < 0;
        var state = new CharacterState
        {
            PY = ground + (spike ? 3f : 0f), IsGrounded = !spike, State = ActionState.Idle,
            DamagePercent = (ushort)Math.Clamp(pct + (int)Math.Round(hit.Damage), 0, 999),
            HitstopTicks = 1, QueuedKBDirZ = 1f, QueuedKBAngle = kb.Angle,
            QueuedKBBase = kb.BaseKnockback, QueuedKBGrowth = kb.KnockbackGrowth,
            QueuedKBDamage = hit.Damage, QueuedKBStun = hit.StunTicks,
            QueuedKBStunGate = hit.StunTicks, QueuedKBFixedHitstunTicks = hit.FixedHitstunTicks,
        };
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        sim.Tick(inputs); // Resolve the real hitstop-expiry queue, including fixed hitstun.
        state = sim.GetState(1);
        var points = new List<Program.TrajPoint> { Program.PointAt(state, 0, ground) };
        ushort stun = state.HitstunTicks;
        int? expiry = stun == 0 ? 0 : null;
        string termination = "simulation-cap";
        int? landed = null;
        for (int tick = 1; tick < 1200; tick++)
        {
            sim.Tick(inputs);
            state = sim.GetState(1);
            points.Add(Program.PointAt(state, tick, ground));
            if (expiry == null && state.HitstunTicks == 0) expiry = tick;
            if (state.IsGrounded && state.HitstunTicks == 0)
            { landed = tick; termination = "landing"; break; }
        }
        return new
        {
            pct, stun, apex = points.Max(p => p.Height), maxTravel = points.Max(p => p.Travel),
            hitstunExpiryTick = expiry, landedTick = landed, termination, points,
        };
    }
}

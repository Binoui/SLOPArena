using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

/// <summary>Provisional shape values. Balance follows the first playable slice.</summary>
public static class MankiMayhemTuning
{
    public const ushort DeckPasses = 6;
    public const ushort CallTicks = 120, MinimumCallTicks = 60, CallReductionPerPass = 10;
    public const ushort WindUpTicks = 24, FlightTicks = 90, VolleyGapTicks = 18;
    public const ushort FuseTicks = 120, MinimumFuseTicks = 60, FuseReductionPerPass = 10;
    public const ushort ScoreTicks = 45, BreakTicks = 60;
    public const ushort SurvivalPoints = 1, LastStandingBonus = 1;
    public const int MaxBombs = 12;
    public const float CageHalfSize = 1.9f, WallHalfThickness = .18f, WallHeight = 6.2f;
    public const byte WallHits = 3, CrackedWallHits = 2;
    public const ushort WallRiseTicks = 18, WallSinkTicks = 30, CageFlightTicks = 210;
    public const float CageBlastRadius = 3.6f, CageBombOffsetZ = .7f, CageBotAttackReach = .85f;
    public const ushort SlamFlashPeriodTicks = 18, SlamFlashOnTicks = 12;
    public const sbyte RingAngle = 20;
    public const ushort SlamGapTicks = 150, SlamPairGapTicks = 42, RingTicks = 150;
    public const float RingSpeed = 9f, RingHalfWidth = .28f, RingHalfHeight = .25f;
    public const float RingDamage = 10f, RingKnockback = 12f, RingGrowth = 6f;
    public const ushort RingHitstun = 24, BotJumpLeadTicks = 12;
    public const float BombRadius = .35f, BlastRadius = 2.5f, BombGravity = 12f;
    public const float BombDamage = 18f, BombKnockback = 20f, BombGrowth = 14f;
    public const sbyte BombAngle = 45;
    public const ushort BombStun = 1, BlastTicks = 2;
    public const float AimJitter = .75f, PlacementMargin = 1f, BotClearance = .75f;
}

public enum MankiMayhemCard : byte { Scatter, Jump, BreakOut }

public readonly record struct MayhemSlam(int Ordinal, ArcadePoint Position, uint Tick);

/// <summary>A current round's landing ring. Tick values are absolute Match ticks.</summary>
public readonly record struct MayhemLandingRing(int Ordinal, ArcadePoint Position, uint LaunchTick,
    uint LandingTick, ushort FuseTicks);

internal static class MankiMayhemRounds
{
    private static readonly MankiMayhemCard[] Deck = { MankiMayhemCard.Scatter, MankiMayhemCard.Jump, MankiMayhemCard.BreakOut };
    public static int DeckSize => Deck.Length;

    public static MankiMayhemCard CardAt(uint seed, ushort round)
    {
        var bag = (MankiMayhemCard[])Deck.Clone();
        uint pass = (uint)(round / bag.Length);
        for (int i = bag.Length - 1; i > 0; i--)
        {
            int j = (int)(Sample(seed, pass, (uint)i) % (uint)(i + 1));
            (bag[i], bag[j]) = (bag[j], bag[i]);
        }
        return bag[round % bag.Length];
    }

    private static uint Sample(uint seed, uint round, uint ordinal)
    {
        uint x = seed ^ unchecked((round + 1) * 0x9e3779b9u) ^ unchecked((ordinal + 1) * 0x85ebca6bu);
        x ^= x >> 16; x = unchecked(x * 0x7feb352du); x ^= x >> 15;
        x = unchecked(x * 0x846ca68bu); return x ^ (x >> 16);
    }

    public static ushort CallDuration(ushort round) => (ushort)Math.Max(MankiMayhemTuning.MinimumCallTicks,
        MankiMayhemTuning.CallTicks - round / DeckSize * MankiMayhemTuning.CallReductionPerPass);

    public static MayhemLandingRing[] Scatter(uint seed, ushort round, uint callTick,
        MankiMayhemDefinition stage, IReadOnlyList<CharacterState> fighters)
    {
        int pass = round / DeckSize;
        int count = Math.Min(MankiMayhemTuning.MaxBombs, fighters.Count + pass);
        var rings = new MayhemLandingRing[count];
        for (int i = 0; i < count; i++)
        {
            var target = fighters[i % fighters.Count];
            uint sample = Sample(seed, round, (uint)i);
            float dx = ((sample & 65535) / 65535f * 2f - 1f) * MankiMayhemTuning.AimJitter;
            float dz = ((sample >> 16) / 65535f * 2f - 1f) * MankiMayhemTuning.AimJitter;
            float margin = MankiMayhemTuning.PlacementMargin;
            var point = new ArcadePoint(Math.Clamp(target.PX + dx, stage.MinX + margin, stage.MaxX - margin),
                stage.DeckTop, Math.Clamp(target.PZ + dz, stage.MinZ + margin, stage.MaxZ - margin));
            uint launch = callTick + CallDuration(round) + (uint)(i * MankiMayhemTuning.VolleyGapTicks);
            rings[i] = new(i, point, launch, launch + MankiMayhemTuning.FlightTicks - 1,
                (ushort)Math.Max(MankiMayhemTuning.MinimumFuseTicks,
                    MankiMayhemTuning.FuseTicks - pass * MankiMayhemTuning.FuseReductionPerPass));
        }
        return rings;
    }

    public static MayhemSlam[] Jump(ushort round, uint callTick, MankiMayhemDefinition stage)
    {
        int count = round / DeckSize == 0 ? 2 : 4;
        var slams = new MayhemSlam[count];
        for (int i = 0; i < count; i++)
        {
            int offset = count == 2 ? i * MankiMayhemTuning.SlamGapTicks
                : i / 2 * MankiMayhemTuning.SlamGapTicks + i % 2 * MankiMayhemTuning.SlamPairGapTicks;
            slams[i] = new(i, new(0, stage.DeckTop + MankiMayhemTuning.RingHalfHeight, 0),
                callTick + CallDuration(round) + (uint)offset);
        }
        return slams;
    }

    public static void Slam(MayhemSlam slam, ushort round, SpellResolver resolver)
        => resolver.Spawn(new Hitbox
        {
            OwnerId = MankiMayhemSimulation.HostEntityId, AttackSequence = (byte)round,
            ActivationId = (ulong)round + 1, VisualOperationIndex = slam.Ordinal,
            X = slam.Position.X, Y = slam.Position.Y, Z = slam.Position.Z,
            Shape = HitboxShape.Ring, RingSpeed = MankiMayhemTuning.RingSpeed,
            RingHalfWidth = MankiMayhemTuning.RingHalfWidth, RingHalfHeight = MankiMayhemTuning.RingHalfHeight,
            DurationTicks = MankiMayhemTuning.RingTicks, Unblockable = true, HitsMultipleOpponents = true,
            Damage = MankiMayhemTuning.RingDamage, BaseKnockback = MankiMayhemTuning.RingKnockback,
            KnockbackGrowth = MankiMayhemTuning.RingGrowth, KnockbackAngle = MankiMayhemTuning.RingAngle,
            FixedHitstunTicks = MankiMayhemTuning.RingHitstun, StunTicks = 1,
        });

    public static MayhemLandingRing[] BreakOut(uint callTick, MankiMayhemDefinition stage,
        IReadOnlyList<CharacterState> fighters)
    {
        var bombs = new MayhemLandingRing[fighters.Count];
        for (int i = 0; i < fighters.Count; i++)
        {
            var fighter = fighters[i];
            uint launch = callTick + MankiMayhemTuning.WindUpTicks;
            bombs[i] = new(i, new(fighter.PX, stage.DeckTop, fighter.PZ + MankiMayhemTuning.CageBombOffsetZ), launch,
                launch + MankiMayhemTuning.CageFlightTicks - 1, 1);
        }
        return bombs;
    }

    public static void Launch(MayhemLandingRing ring, ushort round, MankiMayhemDefinition stage, SpellResolver resolver,
        bool cage = false)
    {
        var target = new ArcadePoint(ring.Position.X, ring.Position.Y + MankiMayhemTuning.BombRadius, ring.Position.Z);
        CombatMath.ComputeProjectileLaunch(stage.HostOrigin, target, MankiMayhemTuning.BombGravity,
            cage ? MankiMayhemTuning.CageFlightTicks : MankiMayhemTuning.FlightTicks, out var velocity);
        resolver.Spawn(new Hitbox
        {
            OwnerId = MankiMayhemSimulation.HostEntityId, AttackSequence = (byte)round,
            ActivationId = (ulong)round + 1, VisualOperationIndex = ring.Ordinal,
            X = stage.HostOrigin.X, Y = stage.HostOrigin.Y, Z = stage.HostOrigin.Z,
            VX = velocity.X, VY = velocity.Y, VZ = velocity.Z, Gravity = MankiMayhemTuning.BombGravity,
            Radius = MankiMayhemTuning.BombRadius, DurationTicks = cage ? MankiMayhemTuning.CageFlightTicks : MankiMayhemTuning.FlightTicks,
            StickyFuseTicks = cage ? (ushort)0 : ring.FuseTicks,
            Explosion = new ProjectileExplosion
            {
                Radius = cage ? MankiMayhemTuning.CageBlastRadius : MankiMayhemTuning.BlastRadius, Damage = MankiMayhemTuning.BombDamage,
                Knockback = new KnockbackData { Profile = KnockbackProfile.Custom, Angle = MankiMayhemTuning.BombAngle, BaseKnockback = MankiMayhemTuning.BombKnockback,
                    KnockbackGrowth = MankiMayhemTuning.BombGrowth },
                StunTicks = MankiMayhemTuning.BombStun, DurationTicks = MankiMayhemTuning.BlastTicks,
                ExplosionPresentationId = "mayhem.scatter.blast", PresentationAttackSequence = (byte)round,
                PresentationOperationIndex = ring.Ordinal,
            },
        });
    }
}

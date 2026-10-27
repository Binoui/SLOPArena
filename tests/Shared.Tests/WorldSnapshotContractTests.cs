using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// The World Snapshot contract, field by field (ADR-0039): every value-type codec and every
/// ability runtime must round-trip each field it declares. A field added without a codec or
/// snapshot override fails here even if no fight happens to exercise it.
/// </summary>
public class WorldSnapshotContractTests
{
    private static readonly CharacterClass[] Roster =
        { CharacterClass.FightGuy, CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bonk, CharacterClass.Bashie };

    /// <summary>Wiring re-attached by the simulation on restore; never part of a snapshot.</summary>
    private static readonly HashSet<string> Wiring = new()
    {
        "OwnerSimulation", "PresentationSink", "SimulationStates", "Resolver", "BakedData",
        "CharacterDef", "Arena",
    };

    [Fact]
    public void MayhemDirectorSnapshotCoversCursorPlayersBenchWithdrawalsAndLandingRings()
    {
        var stage = MankiMayhemTests.Arena().Arcade!.Mayhem!;
        var original = new MankiMayhemSimulation(stage);
        original.Apply(new(new(81, 2, MankiMayhemCard.Scatter, MankiMayhemPhase.Pattern, 12, 1),
            new[] { new MayhemPlayer(1, 3, 2, 1, true, false), new MayhemPlayer(2, 1, 1, 0, true, true) },
            new[] { new MayhemLandingRing(0, new(3, 0, 4), 200, 289, 120) }, Array.Empty<MayhemSlam>(), Array.Empty<RuntimeSolid>()));
        var w = new SnapshotWriter(); original.WriteSnapshot(w);
        var r = new SnapshotReader(w.ToArray());
        var copy = new MankiMayhemSimulation(stage);
        copy.Apply(copy.ReadSnapshot(r, new ulong[] { 1, 2 }));
        Assert.True(r.AtEnd);
        Assert.Equal(original.State, copy.State); Assert.Equal(original.Players, copy.Players); Assert.Equal(original.Rings, copy.Rings);
        var fields = typeof(MankiMayhemSimulation).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => !f.IsInitOnly).Select(f => f.Name).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "_rings", "_slams", "_state" }, fields);
    }

    [Fact]
    public void MayhemSnapshotPresenceAndMalformedCursorAreRejectedAtomically()
    {
        var run = MankiMayhemTests.NewMatch(); MankiMayhemTests.Step(run);
        var practice = new ServerSimulation(MankiMayhemTests.Arena(), NoWinMatchRule.Instance);
        for (ulong id = 1; id <= 4; id++) practice.RegisterEntity(id, TestHelpers.EngineDef, run.GetState(id));
        var before = run.CaptureSnapshot();
        Assert.Throws<InvalidDataException>(() => run.RestoreSnapshot(practice.CaptureSnapshot()));
        Assert.True(before.ContentEquals(run.CaptureSnapshot()));
        var director = (MankiMayhemSimulation)typeof(ServerSimulation)
            .GetField("_mayhem", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(run)!;
        var payload = new SnapshotWriter(); director.WriteSnapshot(payload);
        var malformed = before.Data.ToArray();
        malformed[malformed.Length - payload.ToArray().Length + 7] = byte.MaxValue;
        Assert.Throws<InvalidDataException>(() => run.RestoreSnapshot(new WorldSnapshot(malformed)));
        Assert.True(before.ContentEquals(run.CaptureSnapshot()));
        var bytes = before.Data.ToArray(); bytes[0] = 9;
        Assert.Throws<InvalidDataException>(() => run.RestoreSnapshot(new WorldSnapshot(bytes)));
        Assert.True(before.ContentEquals(run.CaptureSnapshot()));
    }

    [Fact]
    public void CharacterStateCodecCoversEveryField()
    {
        var original = (CharacterState)Fill(typeof(CharacterState), new Random(1));
        var w = new SnapshotWriter();
        SnapshotCodecs.Write(w, original);
        var r = new SnapshotReader(w.ToArray());
        var copy = SnapshotCodecs.ReadCharacterState(r);
        Assert.True(r.AtEnd);
        AssertDeepEqual(original, copy, nameof(CharacterState));
    }

    [Fact]
    public void FingerGunsRuntimeRoundTripsEveryField()
    {
        var operation = new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            CharacterPackageCompiler.FingerGunsCapabilityId,
            CharacterPackageCompiler.FingerGunsCapabilityVersion, FingerGunsTests.Parameters);
        var original = Create(operation);
        FillAbility(original, new Random(93));
        var copy = Create(operation);
        RoundTrip(original, copy);
        AssertAbilityEqual(original, copy);
    }

    [Fact]
    public void InputStateCodecCoversEveryField()
    {
        var original = (InputState)Fill(typeof(InputState), new Random(2));
        var w = new SnapshotWriter();
        SnapshotCodecs.Write(w, original);
        var r = new SnapshotReader(w.ToArray());
        var copy = SnapshotCodecs.ReadInputState(r);
        Assert.True(r.AtEnd);
        AssertDeepEqual(original, copy, nameof(InputState));
    }

    [Fact]
    public void HitboxCodecCoversEveryFieldAndRelinksOwnerContent()
    {
        var content = BuiltInContentResolver.Resolve(CharacterClass.Wibou);
        var original = (Hitbox)Fill(typeof(Hitbox), new Random(3));
        original.Def = content.Definition;
        original.Baked = content.BakedAnimation;
        (CharacterDefinition?, BakedAnimationData?) Owner(ulong id)
            => id == original.OwnerId ? (content.Definition, content.BakedAnimation) : (null, null);

        var w = new SnapshotWriter();
        SnapshotCodecs.Write(w, original, Owner);
        var r = new SnapshotReader(w.ToArray());
        var copy = SnapshotCodecs.ReadHitbox(r, Owner);
        Assert.True(r.AtEnd);
        AssertDeepEqual(original, copy, nameof(Hitbox));

        var foreign = original;
        foreign.Def = BuiltInContentResolver.Resolve(CharacterClass.Bonk).Definition;
        Assert.Throws<InvalidOperationException>(() => SnapshotCodecs.Write(new SnapshotWriter(), foreign, Owner));
    }

    [Fact]
    public void EveryRosterCapabilityRoundTripsEveryField()
    {
        var covered = new HashSet<Type>();
        int seed = 10;
        foreach (var operation in RosterCapabilityOperations())
        {
            var original = Create(operation);
            FillAbility(original, new Random(seed++));
            var copy = Create(operation);
            RoundTrip(original, copy);
            AssertAbilityEqual(original, copy);
            covered.Add(original.GetType());
        }
        Assert.NotEmpty(covered);
    }

    [Fact]
    public void CookedTimelineRuntimeRoundTripsEveryField()
    {
        var rng = new Random(77);
        int checkedSlots = 0;
        foreach (var cls in Roster)
        {
            var def = BuiltInContentResolver.Resolve(cls).Definition;
            foreach (var slot in def.CookedSlots!)
            {
                var names = slot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray();
                var original = new CookedTimelineAbility(slot);
                FillAbility(original, rng, skip: "_capabilities");
                int stage = rng.Next(slot.Timeline.Stages.Count);
                SetField(original, "_stageIndex", stage);
                SetField(original, "_operationCursor", rng.Next(slot.Timeline.Stages[stage].Operations.Count + 1));
                var copy = new CookedTimelineAbility(slot);
                RoundTrip(original, copy);
                AssertAbilityEqual(original, copy);
                checkedSlots++;
            }
        }
        Assert.True(checkedSlots >= 32);
    }

    [Fact]
    public void EveryAbilityTypeIsReachableThroughRosterContent()
    {
        // A new ability or capability type must be covered by the round-trip tests above.
        var reachable = RosterCapabilityOperations().Select(op => Create(op).GetType()).ToHashSet();
        reachable.Add(typeof(CookedTimelineAbility));
        var all = typeof(ServerAbility).Assembly.GetTypes()
            .Where(t => typeof(ServerAbility).IsAssignableFrom(t) && !t.IsAbstract)
            .ToHashSet();
        Assert.Empty(all.Except(reachable).Select(t => t.Name));
    }

    private static IEnumerable<CookedStartCapabilityOperation> RosterCapabilityOperations()
    {
        foreach (var cls in Roster)
            foreach (var slot in BuiltInContentResolver.Resolve(cls).Definition.CookedSlots!)
                foreach (var stage in slot.Timeline.Stages)
                    foreach (var operation in stage.Operations)
                        if (operation is CookedStartCapabilityOperation capability)
                            yield return capability;
        // Public engine capabilities no roster fighter uses yet are covered through their fixtures.
        yield return new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            CharacterPackageCompiler.SteeredChargeCapabilityId,
            CharacterPackageCompiler.SteeredChargeCapabilityVersion, SteeredChargeTests.Parameters);
        yield return new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            CharacterPackageCompiler.ChargedStreamCapabilityId,
            CharacterPackageCompiler.ChargedStreamCapabilityVersion, ChargedStreamTests.Parameters);
        yield return new CookedStartCapabilityOperation(0, AuthoringUnit.Ticks,
            CharacterPackageCompiler.FingerGunsCapabilityId,
            CharacterPackageCompiler.FingerGunsCapabilityVersion, FingerGunsTests.Parameters);
    }

    private static ServerAbility Create(CookedStartCapabilityOperation operation)
    {
        Assert.True(InternalCapabilityRegistry.TryCreate(operation.CapabilityId, operation.CapabilityVersion,
            operation.Parameters, out var capability), operation.CapabilityId);
        return capability;
    }

    private static void RoundTrip(ServerAbility original, ServerAbility copy)
    {
        var w = new SnapshotWriter();
        original.WriteSnapshot(w);
        var r = new SnapshotReader(w.ToArray());
        copy.ReadSnapshot(r);
        Assert.True(r.AtEnd, $"{original.GetType().Name} left unread snapshot data");
    }

    private static IEnumerable<FieldInfo> InstanceFields(Type type)
    {
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                yield return field;
    }

    private static bool IsWiring(FieldInfo field)
        => Wiring.Contains(field.Name.TrimStart('<').Split('>')[0]);

    private static void FillAbility(ServerAbility ability, Random rng, string? skip = null)
    {
        foreach (var field in InstanceFields(ability.GetType()))
        {
            if (field.IsInitOnly || IsWiring(field) || field.Name == skip)
                continue;
            field.SetValue(ability, Fill(field.FieldType, rng));
        }
    }

    private static void SetField(object target, string name, object value)
        => InstanceFields(target.GetType()).Single(f => f.Name == name).SetValue(target, value);

    private static void AssertAbilityEqual(ServerAbility original, ServerAbility copy)
    {
        foreach (var field in InstanceFields(original.GetType()))
        {
            if (IsWiring(field)) continue;
            AssertDeepEqual(field.GetValue(original), field.GetValue(copy),
                $"{original.GetType().Name}.{field.Name}");
        }
    }

    /// <summary>A distinct, valid value for a snapshot field type; content references stay null.</summary>
    private static object? Fill(Type type, Random rng)
    {
        if (type == typeof(bool)) return rng.Next(2) == 1;
        if (type == typeof(byte)) return (byte)rng.Next(1, 256);
        if (type == typeof(sbyte)) return (sbyte)rng.Next(-128, 128);
        if (type == typeof(short)) return (short)rng.Next(short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)rng.Next(1, ushort.MaxValue);
        if (type == typeof(int)) return rng.Next(int.MinValue, int.MaxValue);
        if (type == typeof(uint)) return (uint)rng.Next(1, int.MaxValue);
        if (type == typeof(long)) return (long)rng.Next() << 20;
        if (type == typeof(ulong)) return ((ulong)rng.Next() << 24) | (ulong)rng.Next(1, 1 << 20);
        if (type == typeof(float)) return (float)(rng.NextDouble() * 200 - 100);
        if (type == typeof(double)) return rng.NextDouble();
        if (type == typeof(string)) return "s" + rng.Next(1000);
        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            return values.GetValue(rng.Next(values.Length));
        }
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return Fill(inner, rng);
        if (type == typeof(HashSet<ulong>))
        {
            var set = new HashSet<ulong>();
            for (int i = rng.Next(1, 4); i > 0; i--) set.Add((ulong)rng.Next(1, 9));
            return set;
        }
        if (type == typeof(Dictionary<byte, HashSet<ulong>>))
        {
            var groups = new Dictionary<byte, HashSet<ulong>>();
            for (int i = rng.Next(1, 3); i > 0; i--) groups[(byte)rng.Next(1, 4)] = (HashSet<ulong>)Fill(typeof(HashSet<ulong>), rng)!;
            return groups;
        }
        if (type.IsArray)
        {
            var element = type.GetElementType()!;
            var array = Array.CreateInstance(element, rng.Next(1, 4));
            for (int i = 0; i < array.Length; i++) array.SetValue(Fill(element, rng), i);
            return array;
        }
        if (type.IsValueType)
        {
            object boxed = Activator.CreateInstance(type)!;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(CharacterDefinition) || field.FieldType == typeof(BakedAnimationData))
                    continue; // content references are re-linked, never written
                field.SetValue(boxed, Fill(field.FieldType, rng));
            }
            if (boxed is Hitbox { Shape: HitboxShape.Ring } ring)
            {
                ring.PreviousRadius = MathF.Abs(ring.PreviousRadius);
                ring.Radius = ring.PreviousRadius + MathF.Abs(ring.Radius);
                ring.RingSpeed = MathF.Abs(ring.RingSpeed);
                ring.RingHalfWidth = MathF.Abs(ring.RingHalfWidth) + .01f;
                ring.RingHalfHeight = MathF.Abs(ring.RingHalfHeight) + .01f;
                return ring;
            }
            return boxed;
        }
        return null;
    }

    private static void AssertDeepEqual(object? expected, object? actual, string path)
    {
        if (expected == null || actual == null)
        {
            Assert.True(expected == null && actual == null, $"{path}: {expected ?? "null"} vs {actual ?? "null"}");
            return;
        }
        var type = expected.GetType();
        Assert.True(type == actual.GetType(), $"{path}: {type.Name} vs {actual.GetType().Name}");
        if (type == typeof(float))
        {
            Assert.True(BitConverter.SingleToInt32Bits((float)expected) == BitConverter.SingleToInt32Bits((float)actual),
                $"{path}: {expected} vs {actual}");
            return;
        }
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
        {
            Assert.True(Equals(expected, actual), $"{path}: {expected} vs {actual}");
            return;
        }
        if (expected is HashSet<ulong> set)
        {
            Assert.True(set.SetEquals((HashSet<ulong>)actual), $"{path}: hit set contents differ");
            return;
        }
        if (expected is IDictionary dictionary)
        {
            var other = (IDictionary)actual;
            Assert.True(dictionary.Count == other.Count, $"{path}: count {dictionary.Count} vs {other.Count}");
            foreach (DictionaryEntry entry in dictionary)
                AssertDeepEqual(entry.Value, other[entry.Key], $"{path}[{entry.Key}]");
            return;
        }
        if (expected is Array array)
        {
            var other = (Array)actual;
            Assert.True(array.Length == other.Length, $"{path}: length {array.Length} vs {other.Length}");
            for (int i = 0; i < array.Length; i++)
                AssertDeepEqual(array.GetValue(i), other.GetValue(i), $"{path}[{i}]");
            return;
        }
        if (expected is IList list)
        {
            var other = (IList)actual;
            Assert.True(list.Count == other.Count, $"{path}: count {list.Count} vs {other.Count}");
            return;
        }
        if (type.IsValueType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                AssertDeepEqual(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}");
            return;
        }
        // Content and parameters: both instances must reference the same immutable object.
        Assert.True(ReferenceEquals(expected, actual), $"{path}: different {type.Name} instances");
    }
}

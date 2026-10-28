using System;
using System.Collections.Generic;

namespace SlopArena.Shared;

public partial class SpellResolver
{
    private readonly List<MankiCanState> _mankiCans = new();
    public IReadOnlyList<MankiCanState> MankiCans => _mankiCans;

    public int MankiCanOccupancy(ulong owner)
    {
        int count = 0;
        foreach (var can in _mankiCans) if (can.OwnerId == owner) count++;
        foreach (var hitbox in _hitboxes) if (hitbox.OwnerId == owner && hitbox.MankiCan.HasValue) count++;
        return count;
    }

    internal void RemoveMankiCans(ulong owner)
    {
        _mankiCans.RemoveAll(can => can.OwnerId == owner);
        _hitboxes.RemoveAll(hitbox => hitbox.OwnerId == owner && (hitbox.MankiCan.HasValue
            || hitbox.IgnitesOwnerCans && hitbox.AttackSlot == AbilitySlots.A));
        _pendingExplosions.RemoveAll(explosion => explosion.ownerId == owner
            && explosion.attackSlot == AbilitySlots.A);
    }

    internal void TickMankiCans()
    {
        for (int i = _mankiCans.Count - 1; i >= 0; i--)
        {
            var can = _mankiCans[i];
            if (can.IsTriggered)
                can = can with { WarningTicksLeft = (ushort)(can.WarningTicksLeft - 1) };
            else
                can = can with { FuseTicksLeft = (ushort)(can.FuseTicksLeft - 1) };
            if (can.FuseTicksLeft == 0 || _mankiCans[i].IsTriggered && can.WarningTicksLeft == 0)
            {
                var blast = _mankiCans[i].IsTriggered ? can.Payload.EarlyBlast : can.Payload.Blast;
                _pendingExplosions.Add((can.X, can.Y, can.Z, blast, can.OwnerId, can.AttackSlot,
                    can.ActivationId, can.Airborne, can.AttackSequence));
                _mankiCans.RemoveAt(i);
            }
            else _mankiCans[i] = can;
        }
    }

    private void IgniteMankiCans(in Hitbox fire)
    {
        if (!fire.IgnitesOwnerCans && !fire.IsCanIgniterOnly) return;
        for (int i = 0; i < _mankiCans.Count; i++)
        {
            var can = _mankiCans[i];
            if (can.OwnerId != fire.OwnerId || can.ActivationId == fire.ActivationId || can.IsTriggered) continue;
            var target = new EntityData { Shape = HitboxShape.Sphere,
                PosX = can.X, PosY = can.Y + MankiCanState.Radius, PosZ = can.Z, Radius = MankiCanState.Radius };
            bool touches = OverlapsAtCurrentPosition(in fire, in target);
            if (!touches && fire.Sweep != null)
                foreach (var pose in fire.Sweep)
                {
                    var sample = fire;
                    sample.X = pose.X; sample.Y = pose.Y; sample.Z = pose.Z;
                    sample.EndX = pose.EndX; sample.EndY = pose.EndY; sample.EndZ = pose.EndZ;
                    if (OverlapsAtCurrentPosition(in sample, in target)) { touches = true; break; }
                }
            if (touches) _mankiCans[i] = can with { WarningTicksLeft = can.Payload.WarningTicks };
        }
    }

    private void LandMankiCan(in Hitbox projectile, float groundY)
    {
        var payload = projectile.MankiCan!.Value;
        _mankiCans.Add(new(projectile.EmissionId, projectile.OwnerId, projectile.ActivationId,
            projectile.AttackSlot, projectile.ActivationAirborne, projectile.AttackSequence,
            projectile.X, groundY, projectile.Z, payload.FuseTicks, 0, payload));
    }

    private void WriteMankiCans(SnapshotWriter w)
    {
        w.WriteInt(_mankiCans.Count);
        foreach (var can in _mankiCans)
        {
            w.WriteULong(can.Id); w.WriteULong(can.OwnerId); w.WriteULong(can.ActivationId);
            w.WriteByte(can.AttackSlot); w.WriteBool(can.Airborne); w.WriteByte(can.AttackSequence);
            w.WriteFloat(can.X); w.WriteFloat(can.Y); w.WriteFloat(can.Z);
            w.WriteUShort(can.FuseTicksLeft); w.WriteUShort(can.WarningTicksLeft);
            SnapshotCodecs.Write(w, can.Payload);
        }
    }

    private static void ReadMankiCans(SnapshotReader r, SnapshotData data,
        Func<ulong, (CharacterDefinition? Def, BakedAnimationData? Baked)> ownerContent)
    {
        int count = r.ReadCount(50);
        var identities = new HashSet<ulong>();
        var ownerCounts = new Dictionary<ulong, int>();
        foreach (var hitbox in data.Hitboxes) identities.Add(hitbox.EmissionId);
        for (int i = 0; i < count; i++)
        {
            var can = new MankiCanState(r.ReadULong(), r.ReadULong(), r.ReadULong(), r.ReadByte(),
                r.ReadBool(), r.ReadByte(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat(),
                r.ReadUShort(), r.ReadUShort(), SnapshotCodecs.ReadMankiCanPayload(r));
            ownerCounts.TryGetValue(can.OwnerId, out int ownerCount);
            if (can.Id == 0 || can.Id > data.LastEmissionId || !identities.Add(can.Id)
                || ownerContent(can.OwnerId).Def == null || ownerCount >= 3
                || can.Payload.WarningTicks == 0 || can.Payload.WarningTicks >= can.Payload.FuseTicks
                || can.FuseTicksLeft == 0 || can.FuseTicksLeft > can.Payload.FuseTicks
                || can.WarningTicksLeft > can.Payload.WarningTicks
                || !float.IsFinite(can.X) || !float.IsFinite(can.Y) || !float.IsFinite(can.Z))
                throw SnapshotReader.Malformed("Manki can");
            ownerCounts[can.OwnerId] = ownerCount + 1;
            data.Cans.Add(can);
        }
    }
}

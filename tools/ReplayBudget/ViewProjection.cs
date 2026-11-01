using System;
using System.IO;
using System.Linq;
using SlopArena.Shared;

namespace SlopArena.ReplayBudget;

internal sealed class ViewProjection(Strings strings)
{
    public long Values { get; private set; }
    public long Overflows { get; private set; }
    public double MaxError { get; private set; }

    public byte[] Write(ServerSimulation sim, WorldFramePacket frame, int fighters, bool quantized)
    {
        if (sim.ArcadeBall != null || sim.Course != null) throw new NotSupportedException("This prototype measures stock mode only.");
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((byte)1); w.Write(quantized); w.Write(frame.Tick); w.Write((byte)frame.MatchState); w.Write((byte)fighters);
        for (ulong id = 1; id <= (ulong)fighters; id++)
        {
            var s = sim.GetState(id);
            w.Write(id);
            w.Write((byte)s.State); w.Write(s.IsGrounded); w.Write(s.AttackSlot); w.Write(s.HitstunTicks);
            w.Write(s.AnimIndex); w.Write(s.AttackElapsedTicks);
            Number(w, s.VX, quantized); Number(w, s.VY, quantized); Number(w, s.VZ, quantized);
            Number(w, s.PX, quantized); Number(w, s.PY, quantized); Number(w, s.PZ, quantized);
            w.Write(s.JumpsLeft); w.Write(s.HitstopTicks); w.Write(s.Deaths); w.Write(s.AttackSequence);
            w.Write(s.HitstunLevel); w.Write(s.FacingYaw); w.Write(s.LandingLagTicks);
            Number(w, s.KVX, quantized); Number(w, s.KVY, quantized); Number(w, s.KVZ, quantized);
            w.Write(s.Form.Active); w.Write(s.Form.Bank1); w.Write(s.Form.Bank3); w.Write(s.Form.Bank4);
            w.Write(s.Form.BankA); w.Write(s.Form.BankE); w.Write(s.Form.BankR);
            w.Write(s.Flight.Active); w.Write(s.Flight.FuelTicksLeft); w.Write(s.Flight.BurstTicksLeft);
            w.Write(s.Flight.LiftSpeed); w.Write(s.Flight.HeadingYaw);
            w.Write(s.ChargeTicks); w.Write(s.AttackPosePitch); w.Write(s.WasAirborneDuringKnockback);
            w.Write(s.WallJumpUsed); w.Write(s.InvincibilityTicks); w.Write(s.InPostHitstunFlight);
            w.Write(s.HitReactionSequence); w.Write(s.DamagePercent); w.Write((byte)Math.Max(0, 250 - s.Deaths));
            w.Write(strings.Id(sim.GetActiveMove(id)?.Slot.Id));
        }
        var shapes = sim.Resolver.GetActiveHitboxes();
        w.Write(checked((ushort)shapes.Count));
        foreach (var h in shapes)
        {
            w.Write(h.EmissionId); w.Write(h.OwnerId); w.Write((byte)h.Shape);
            Number(w, h.X, quantized); Number(w, h.Y, quantized); Number(w, h.Z, quantized);
            Number(w, h.EndX, quantized); Number(w, h.EndY, quantized); Number(w, h.EndZ, quantized);
            Number(w, h.VX, quantized); Number(w, h.VY, quantized); Number(w, h.VZ, quantized);
            w.Write(h.Radius); w.Write(h.PreviousRadius); w.Write(h.RingHalfWidth); w.Write(h.RingHalfHeight);
            w.Write(strings.Id(Slot(sim, h.OwnerId, h.AttackSlot, h.ActivationAirborne, h.Form)));
            w.Write(h.VisualOperationIndex); w.Write(h.AttackSequence);
        }
        w.Write((ushort)0); // Stock matches have no mode objects.
        var events = frame.Events.Where(e => e.MatchTick == frame.Tick).ToArray();
        w.Write(checked((ushort)events.Length));
        foreach (var e in events)
        {
            w.Write(e.EntityId); w.Write(e.OperationIndex); w.Write(strings.Id(e.PresentationId));
            w.Write(e.AttackSequence); w.Write(strings.Id(e.Source.ToString()));
            Number(w, e.WorldX, quantized); Number(w, e.WorldY, quantized); Number(w, e.WorldZ, quantized); w.Write(e.WorldYaw);
            var p = e.Placement;
            w.Write(strings.Id(p.AttachmentMode.ToString())); w.Write(strings.Id(p.BoneId));
            w.Write(p.LocalPositionX); w.Write(p.LocalPositionY); w.Write(p.LocalPositionZ);
            w.Write(p.LocalRotationX); w.Write(p.LocalRotationY); w.Write(p.LocalRotationZ);
            w.Write(p.LocalScaleX); w.Write(p.LocalScaleY); w.Write(p.LocalScaleZ); w.Write(p.DurationTicks);
        }
        var hits = frame.Hits.Where(h => h.MatchTick == frame.Tick).ToArray();
        w.Write(checked((ushort)hits.Length));
        foreach (var h in hits)
        {
            w.Write(h.TargetEntityId); w.Write(h.OwnerEntityId);
            w.Write(strings.Id(Slot(sim, h.OwnerEntityId, h.AttackSlot, h.Airborne, h.Form)));
            w.Write(h.ActivationId); w.Write(h.AttackSequence); w.Write(h.Airborne); w.Write(h.Form);
            w.Write(h.Damage); w.Write(h.DirX); w.Write(h.DirZ); w.Write(h.KnockbackAngle);
            w.Write(strings.Id(h.KnockbackDirection.ToString())); w.Write(h.BaseKnockback); w.Write(h.KnockbackGrowth);
            w.Write(h.StunTicks); w.Write(h.FixedHitstunTicks); w.Write(h.ArmorProtected); w.Write(h.FreezesOwner);
            Number(w, h.HitX, quantized); Number(w, h.HitY, quantized); Number(w, h.HitZ, quantized);
            w.Write(strings.Id(h.HitPresentationId)); w.Write(h.ImpactForce); w.Write(h.HitstopTicks);
            w.Write(h.Blocked); w.Write(h.Unblockable); w.Write(h.ContactTime);
        }
        return stream.ToArray();
    }

    public static void Validate(byte[] record)
    {
        using var r = new BinaryReader(new MemoryStream(record));
        if (r.ReadByte() != 1) throw new InvalidDataException("View version");
        bool quantized = r.ReadBoolean();
        _ = r.ReadUInt32(); _ = r.ReadByte(); int fighters = r.ReadByte();
        void Bytes(int count) { if (r.ReadBytes(count).Length != count) throw new EndOfStreamException(); }
        void Floats(int count) { for (int i = 0; i < count; i++) if (!float.IsFinite(r.ReadSingle())) throw new InvalidDataException("View float"); }
        void Numbers(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!quantized) Floats(1);
                else if (r.ReadInt16() == short.MinValue) Floats(1);
            }
        }
        for (int i = 0; i < fighters; i++)
        {
            Bytes(8 + 1 + 1 + 1 + 2 + 1 + 2); Numbers(6);
            Bytes(1 + 2 + 1 + 1 + 1); Floats(1); Bytes(2); Numbers(3);
            Bytes(1 + 12 + 1 + 2 + 2); Floats(2); Bytes(2); Floats(1);
            Bytes(1 + 1 + 2 + 1 + 2 + 2 + 1 + 2);
        }
        int shapes = r.ReadUInt16();
        for (int i = 0; i < shapes; i++) { Bytes(8 + 8 + 1); Numbers(9); Floats(4); Bytes(2 + 4 + 1); }
        if (r.ReadUInt16() != 0) throw new InvalidDataException("Stock mode objects");
        int events = r.ReadUInt16();
        for (int i = 0; i < events; i++)
        {
            Bytes(8 + 4 + 2 + 1 + 2); Numbers(3); Floats(1); Bytes(2 + 2); Floats(9); Bytes(2);
        }
        int hits = r.ReadUInt16();
        for (int i = 0; i < hits; i++)
        {
            Bytes(8 + 8 + 2 + 8 + 1 + 1 + 1); Floats(3); Bytes(1 + 2); Floats(2);
            Bytes(2 + 2 + 1 + 1); Numbers(3); Bytes(2); Floats(1); Bytes(2 + 1 + 1); Floats(1);
        }
        if (r.BaseStream.Position != record.Length) throw new InvalidDataException("View trailing bytes");
    }

    private static string? Slot(ServerSimulation sim, ulong owner, byte slot, bool air, byte form)
    {
        var def = sim.GetDefinition(owner);
        if (def == null || slot == 0) return null;
        // GetDefinition resolves the live form. This stock roster has no alternate forms.
        if (sim.GetState(owner).Form.Active != form) throw new NotSupportedException("Cross-form semantic lookup needs the base definition.");
        return def.GetCookedSlotAbility(slot, air)?.Id
            ?? throw new InvalidDataException($"No semantic slot for {owner}/{slot}/{air}");
    }

    private void Number(BinaryWriter w, float value, bool quantized)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite view value");
        if (!quantized) { w.Write(value); return; }
        Values++;
        float rounded = MathF.Round(value * 100f);
        // Reserve -32768 as an escape, so overflow never silently clamps the view.
        if (rounded <= short.MinValue || rounded > short.MaxValue)
        {
            Overflows++; w.Write(short.MinValue); w.Write(value);
        }
        else
        {
            short encoded = (short)rounded; w.Write(encoded);
            MaxError = Math.Max(MaxError, Math.Abs(value - encoded / 100f));
            if (Math.Abs(value - encoded / 100f) > .0051f) throw new InvalidDataException("Quantization error bound");
        }
    }
}

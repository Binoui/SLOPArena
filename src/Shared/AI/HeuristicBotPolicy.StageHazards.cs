using System;

namespace SlopArena.Shared.AI;

public sealed partial class HeuristicBotPolicy
{
    private static bool TryAvoidStageHazard(in CharacterState self, CharacterDefinition def,
        in ArenaDefinition arena, BotMemory memory, out Footstep step, out InputState input)
    {
        step = default; input = default;
        var view = memory.VisibleHazards;
        if (view == null) return false;
        // Reserve stopping/decision room as well as the hurt capsules. This keeps chase and
        // attack selection outside the danger until the delayed cue has actually ended.
        float clearance = .25f + def.Movement.RunSpeed * .25f;
        var body = ServerSimulation.BuildEntitiesFromState(self, def, null!, "idle", 0, self.EntityId);
        foreach (var cue in view.Cues)
        {
            if (view.Tick >= cue.EndTick) continue;
            if (cue.Cells != null)
                return TryAvoidCells(self, def, arena, memory, cue, out step, out input);
            var h = cue.Geometry;
            bool danger = false;
            float margin = def.CapsuleRadius + clearance;
            foreach (var capsule in body)
            {
                var grown = capsule;
                grown.Radius += clearance;
                if (SpellResolver.CapsuleCollision(h, grown, out _, out _, out _, out _, out _, out _, out _))
                    danger = true;
                // An airborne approach must also keep its landing footprint outside the cue.
                float drop = MathF.Max(0, self.PY - h.Y);
                grown.PosY -= drop; grown.EndY -= drop;
                if (SpellResolver.CapsuleCollision(h, grown, out _, out _, out _, out _, out _, out _, out _))
                    danger = true;
                margin = MathF.Max(margin, MathF.Max(MathF.Abs(capsule.PosX - self.PX),
                    MathF.Abs(capsule.EndX - self.PX)) + grown.Radius);
            }
            if (!danger) continue;
            // Escape the union, not just the first overlapping section of the action.
            float low = MathF.Min(h.X, h.EndX) - h.Radius, high = MathF.Max(h.X, h.EndX) + h.Radius;
            foreach (var section in view.Cues)
                if (section.Ordinal == cue.Ordinal && view.Tick < section.EndTick)
                {
                    low = MathF.Min(low, MathF.Min(section.Geometry.X, section.Geometry.EndX) - section.Geometry.Radius);
                    high = MathF.Max(high, MathF.Max(section.Geometry.X, section.Geometry.EndX) + section.Geometry.Radius);
                }
            // Once clear, wait at the safe side. Reversing toward an unreachable escape
            // goal would send a chase or stage detour back through the live action.
            float bodyMargin = margin - clearance + .25f;
            if (self.PX < low - bodyMargin || self.PX > high + bodyMargin)
            { input.ShieldHeld = self.IsGrounded; return true; }
            float preferred = self.PX - low < high - self.PX ? -1f : 1f;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float dir = attempt == 0 ? preferred : -preferred;
                float distance = dir < 0 ? self.PX - low + margin : high - self.PX + margin;
                var footing = StageWalk.Probe(self, def, arena, dir, 0, distance, JumpRise(def));
                if (footing.End is WalkEnd.Clear or WalkEnd.Drop)
                { step = new Footstep(dir, 0, distance); return true; }
            }
            if (self.JumpsLeft > 0)
            { input.Jump = true; return true; }
        }
        return false;
    }
    private static bool TryAvoidCells(in CharacterState self, CharacterDefinition def, in ArenaDefinition arena,
        BotMemory memory, StageHazards.HazardCue cue, out Footstep step, out InputState input)
    {
        step = default; input = default;
        var cells = cue.Cells!; float margin = def.CapsuleRadius + .15f;
        bool Lit(int index)
        {
            foreach (int i in cells.Indices) if (i == index) return true;
            return false;
        }
        bool Danger(float x, float z)
        {
            foreach (int i in cells.Indices)
            {
                var p = cells.Anchor.Points[i];
                if (MathF.Abs(x - p.X) < cells.Anchor.Radius + margin && MathF.Abs(z - p.Z) < cells.Anchor.Radius + margin) return true;
            }
            return false;
        }
        // This branch reads only the delayed, public cell selection. Dark pads and the
        // roof surrounding the floor are safe waiting places for the rest of the warning.
        if (!Danger(self.PX, self.PZ)) { input.ShieldHeld = self.IsGrounded; return true; }
        float best = float.MaxValue, targetX = self.PX, targetZ = self.PZ;
        float sx = self.PX, sz = self.PZ;
        void Candidate(float x, float z)
        {
            if (Danger(x, z)) return;
            float dx = x - sx, dz = z - sz, d = MathF.Sqrt(dx * dx + dz * dz);
            if (d < best) { best = d; targetX = x; targetZ = z; }
        }
        foreach (var p in cells.Anchor.Points)
        {
            Candidate(p.X - cells.Anchor.Radius - margin - .05f, sz);
            Candidate(p.X + cells.Anchor.Radius + margin + .05f, sz);
            Candidate(sx, p.Z - cells.Anchor.Radius - margin - .05f);
            Candidate(sx, p.Z + cells.Anchor.Radius + margin + .05f);
        }
        for (int i = 0; i < cells.Anchor.Points.Count; i++)
            if (!Lit(i)) Candidate(cells.Anchor.Points[i].X, cells.Anchor.Points[i].Z);
        if (best < float.MaxValue)
        {
            float dx = (targetX - sx) / MathF.Max(best, .001f), dz = (targetZ - sz) / MathF.Max(best, .001f);
            var footing = StageWalk.Probe(self, def, arena, dx, dz, best, JumpRise(def));
            uint now = memory.VisibleHazards!.Tick + (uint)BotDifficultyProfile.ForDifficulty(memory.Difficulty).ReactionDelayTicks;
            float walkTicks = (best / def.Movement.RunSpeed) * 60 + 12;
            if (footing.End is WalkEnd.Clear or WalkEnd.Drop && now + walkTicks < cue.ImpactTick)
            { step = new Footstep(dx, dz, best); return true; }
            // Jump late enough to remain airborne over the drop, holding through squat.
            if (self.JumpsLeft > 0 && now + 24 >= cue.ImpactTick) { input.Jump = true; return true; }
        }
        return true;
    }

}

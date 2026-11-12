using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.Replays
{
    /// <summary>
    /// The roster resolved against today's content, plus the cached move lookup. Built once per session.
    /// Move policy (replay-file.md): (package, form, name) first, then (package, form, slot address), else Missing.
    /// </summary>
    internal sealed class ReplayCastIndex
    {
        internal readonly record struct Resolved(CookedSlotDefinition? Slot, byte WireSlot, ReplayMoveMatch Match)
        {
            internal static readonly Resolved None = new(null, 0, ReplayMoveMatch.None);
            internal static readonly Resolved Missing = new(null, 0, ReplayMoveMatch.Missing);
        }

        readonly Dictionary<ulong, ReplayCastMember> byEntity = new();
        readonly Dictionary<(ulong Entity, string Package, string Form, string Name, string Slot, bool Air), Resolved> moves = new();

        internal IReadOnlyList<ReplayCastMember> Members { get; }

        ReplayCastIndex(IReadOnlyList<ReplayCastMember> members)
        {
            Members = members;
            foreach (var m in members) byEntity.Add(m.EntityId, m);
        }

        /// <summary>Null only when a recorded package is missing and so is the stand-in.</summary>
        internal static ReplayCastIndex? Build(ReplayHeader header, ReplayContent content)
        {
            var members = new List<ReplayCastMember>(header.Roster.Count);
            foreach (var p in header.Roster)
            {
                var entry = content.Catalog.ResolvePackage(p.Package.PackageId);
                var kind = entry == null ? ReplayCastKind.StandIn : entry.Identity == p.Package ? ReplayCastKind.Recorded : ReplayCastKind.Today;
                entry ??= content.Catalog.ResolvePackage(content.StandInPackageId);
                if (entry == null) return null;
                members.Add(new ReplayCastMember(p.EntityId, p, entry, kind));
            }
            return new ReplayCastIndex(members.AsReadOnly());
        }

        internal ReplayCastMember? Member(ulong entityId) => byEntity.TryGetValue(entityId, out var m) ? m : null;

        /// <summary>The registered form for a recorded form ID; a form today's fighter lacks plays its base form.</summary>
        internal byte Form(ReplayCastMember member, string formId)
            => formId == "alternate" && member.Kind != ReplayCastKind.StandIn && member.Content.Definition.AlternateForm != null ? (byte)1 : (byte)0;

        /// <summary>A stand-in resolves nothing: its package is not the recorded one, so its moves are Missing.</summary>
        internal Resolved Move(ulong entityId, string package, string form, string name, string slotId, bool airborne)
        {
            if (name.Length == 0 && slotId.Length == 0) return Resolved.None;
            bool air = CanonicalSlotProjection.TryGet(slotId, out var recorded) ? recorded.IsAirborne : airborne;
            var key = (entityId, package, form, name, slotId, air);
            if (moves.TryGetValue(key, out var found)) return found;
            found = Lookup(entityId, package, form, name, slotId, air);
            moves.Add(key, found);
            return found;
        }

        Resolved Lookup(ulong entityId, string package, string form, string name, string slotId, bool air)
        {
            if (Member(entityId) is not { Kind: not ReplayCastKind.StandIn } member || member.Recorded.Package.PackageId != package)
                return Resolved.Missing;
            byte formIndex = form switch { "base" => 0, "alternate" => 1, _ => byte.MaxValue };
            var registered = member.Content.Definition;
            if (formIndex == byte.MaxValue || (formIndex == 1 && registered.AlternateForm == null)) return Resolved.Missing;
            var slots = registered.ForForm(formIndex).CookedSlots;
            if (slots == null) return Resolved.Missing;
            CookedSlotDefinition? byName = null;
            foreach (var slot in slots)
            {
                if (slot == null || slot.Name != name || name.Length == 0) continue;
                if (slot.Id == slotId) { byName = slot; break; }
                if (byName == null || (byName.IsAir != air && slot.IsAir == air)) byName = slot;
            }
            if (byName != null) return With(byName, ReplayMoveMatch.ByName);
            if (CanonicalSlotProjection.TryGet(slotId, out var address) && address.Ordinal < slots.Count && slots[address.Ordinal] is { } bySlot)
                return With(bySlot, ReplayMoveMatch.BySlot);
            return Resolved.Missing;
        }

        static Resolved With(CookedSlotDefinition slot, ReplayMoveMatch match)
            => new(slot, CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slot.Ordinal]), match);

        internal IReadOnlyList<ReplayMoveLabel> Labels(ReplayViewTick view)
        {
            var labels = new ReplayMoveLabel[view.Fighters.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                var f = view.Fighters[i];
                var move = Move(f.EntityId, f.PackageId, f.MoveFormId, f.MoveName, f.SlotId, f.ActivationAirborne);
                labels[i] = new ReplayMoveLabel(f.EntityId, f.MoveName, move.Match);
            }
            return labels;
        }
    }

    /// <summary>The View track's display world: the cast registered on today's arena, never ticked.</summary>
    internal static class ReplayShells
    {
        internal static ServerSimulation Create(ReplayHeader header, ArenaDefinition arena, ReplayCastIndex cast)
        {
            // The recorded format's own rule, so the mode simulations exist for the decoder to restore. When today's arena
            // or rules no longer admit the format, only the fighters show, on a rule that eliminates no one.
            IMatchRule rule;
            if (MatchFormats.TryParseWire(header.Rules.FormatId, out var format) && format == MatchFormat.Stock)
                rule = format.CreateRule(header.Rules.MaxStocks);
            else
                try { rule = ReplayExact.Rule(header, arena); }
                catch (ArgumentException) { rule = NoWinMatchRule.Instance; }
            var shell = new ServerSimulation(arena, rule) { ReplayDisplay = true };
            foreach (var member in cast.Members)
            {
                var state = new CharacterState { EntityId = member.EntityId };
                state.Form.Active = cast.Form(member, member.Recorded.FormId);
                shell.RegisterEntity(member.EntityId, member.Content.Definition, state, member.Content.BakedAnimation);
            }
            return shell;
        }
    }

    /// <summary>
    /// The Exact gate's environment from today's content. The GameHost hashes the whole roster's handle map with the
    /// Match arena (MultiMatchOrchestrator), so a build with the same roster and arena reproduces its hash.
    /// </summary>
    internal static class ReplayExactEnvironment
    {
        internal static ReplayEnvironment For(ReplayContent content, ReplayArena arena, ReplayHeader? header = null)
        {
            string hash;
            try
            {
                if (header?.ContentMap is { } map)
                {
                    var entries = new List<MatchContentEntry>();
                    foreach (var record in map.Entries)
                    {
                        var entry = content.Catalog.ResolvePackage(record.Identity.PackageId);
                        if (entry?.Identity != record.Identity || entry.DisplayName != record.DisplayName)
                            throw new ArgumentException("Recorded content is unavailable.");
                        entries.Add(new MatchContentEntry(record.Handle, entry.LegacySelector, entry.Identity,
                            entry.DisplayName, entry.Definition, entry.BakedAnimation, entry.CookedCharacterPackage, entry.IsBundled));
                    }
                    if (map.Arena != new MatchArenaRecord(arena.Definition.Name, arena.Digest))
                        throw new ArgumentException("Recorded arena is unavailable.");
                    content = content with { Catalog = new MatchContentCatalog(entries) };
                    hash = MatchContentHandleMapCodec.HashLocalContent(map);
                }
                else
                {
                    hash = SteamMatchDescriptor.HashContent(MatchContentHandleMap.CaptureBundled(content.Catalog,
                        new MatchArenaRecord(arena.Definition.Name, arena.Digest)));
                }
            }
            catch (ArgumentException)
            {
                hash = "";
            }
            return new ReplayEnvironment(hash, content.Catalog,
                new Dictionary<string, ArenaDefinition> { [arena.Definition.Name] = arena.Definition },
                new Dictionary<string, string> { [arena.Definition.Name] = arena.Digest });
        }
    }
}

using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.Entities;
using SlopArena.Client.Simulation;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlopArena.Client.Combat
{
    /// <summary>
    /// Bashie's Rescue Ring line (ADR-0048), drawn from authoritative state and events only: a rope from her hand to
    /// the ring in flight, to the fighter she is reeling in, or to the empty ring coming home after a miss, and her
    /// life ring around a fighter while it is Reeled. The flying ring itself is the projectile visual of her slot.
    /// </summary>
    public sealed class TetherRopePresenter : MonoBehaviour
    {
        private const string HandBone = "bone.right-hand";
        private const string HipsBone = "bone.hips";
        private const float RopeWidth = 0.035f;
        private const int RopePoints = 12;
        private const float Sag = 0.18f;

        private enum Phase : byte { None, Flight, Reel, Retract }

        private sealed class Line
        {
            public Phase Phase;
            public bool Placed;
            public GameObject? GraspPrefab, Grasp;
            public Vector3 Target;
            public ulong Partner;
            public Vector3 RetractFrom;
            public float RetractStart, RetractSeconds;
            public LineRenderer Rope = null!;
            public GameObject? EmptyRing;
        }

        private readonly Dictionary<ulong, (PlayerRenderer Renderer, ProjectilePresentation? Ring, CharacterAnimationCatalog Catalog)> _fighters = new();
        private readonly Dictionary<ulong, Line> _lines = new();
        private readonly Dictionary<ulong, GameObject> _worn = new();
        private readonly Dictionary<ulong, ulong> _wornFrom = new();
        // Predicted worlds re-simulate ticks: a retract already drawn must not restart.
        private readonly HashSet<PresentationEventKey> _retracts = new();
        private Material? _ropeMaterial;

        public void Register(ulong entityId, PlayerRenderer renderer, CharacterAnimationCatalog catalog)
            => _fighters[entityId] = (renderer, catalog?.PresentationProfile?.FindProjectile(AbilitySlots.A, false), catalog);

        /// <summary>Reads this tick's predicted or authoritative world: who is throwing, reeling or reeling back.</summary>
        public void Present(ISimulationBridge bridge, IReadOnlyList<TimelinePresentationEvent> events)
        {
            foreach (var (id, fighter) in _fighters)
            {
                if (fighter.Renderer == null) continue;
                var state = bridge.GetState(id);
                var parameters = Tether(bridge.GetActiveMove(id)?.Slot);
                bool tether = parameters != null && state.State == ActionState.Attacking;
                var line = LineFor(id);
                line.Placed = tether && parameters.AtTarget != null;
                line.GraspPrefab = line.Placed ? GraspPrefab(fighter.Catalog, parameters.AtTarget.CatchPresentationId) : null;
                if (tether && state.InteractionId != 0
                    && state.InteractionPhase == (byte)DefenseInteractionPhase.Reeling)
                {
                    line.Phase = Phase.Reel;
                    line.Partner = state.InteractionPartnerId;
                    if (!line.Placed) _wornFrom[line.Partner] = id;
                    else _wornFrom.Remove(line.Partner);
                }
                else if (tether && !line.Placed && RingInFlight(bridge.Resolver, id, out var ring))
                {
                    line.Phase = Phase.Flight;
                    line.Target = ring;
                }
                // The tick a ring is spent, the catch or the retract it causes arrives on the next one.
                else if (tether && line.Phase is not (Phase.Retract or Phase.Flight))
                    line.Phase = Phase.None;
                else if (!tether)
                    line.Phase = Phase.None;
            }
            if (events != null)
                foreach (var e in events)
                    if (e.Source == PresentationEventSource.TetherRetract && _retracts.Add(e.Key))
                    {
                        var line = LineFor(e.EntityId);
                        line.Phase = Phase.Retract;
                        line.RetractFrom = new Vector3(e.WorldX, e.WorldY, e.WorldZ);
                        line.RetractStart = Time.time;
                        line.RetractSeconds = Mathf.Max(1, e.Placement?.DurationTicks ?? 1) / 60f;
                    }
            foreach (var (id, fighter) in _fighters)
            {
                bool reeled = fighter.Renderer != null && bridge.GetState(id).State == ActionState.Reeled
                    && _wornFrom.TryGetValue(id, out var from) && _lines.TryGetValue(from, out var source) && !source.Placed;
                if (reeled && !_worn.ContainsKey(id) && NewRing(id) is { } ring)
                    _worn[id] = ring;
                else if (!reeled && _worn.Remove(id, out var worn))
                {
                    Destroy(worn);
                    _wornFrom.Remove(id);
                }
            }
        }

        public void Clear()
        {
            foreach (var line in _lines.Values)
            {
                if (line.Rope != null) Destroy(line.Rope.gameObject);
                if (line.Grasp != null) Destroy(line.Grasp);
                if (line.EmptyRing != null) Destroy(line.EmptyRing);
            }
            foreach (var worn in _worn.Values)
                if (worn != null) Destroy(worn);
            _lines.Clear();
            _worn.Clear();
            _wornFrom.Clear();
            _retracts.Clear();
        }

        private void OnDestroy()
        {
            Clear();
            _fighters.Clear();
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
        }

        private void LateUpdate()
        {
            foreach (var (id, line) in _lines)
            {
                var hand = _fighters.TryGetValue(id, out var owner) && owner.Renderer != null
                    ? owner.Renderer.ResolvePresentationBone(HandBone) : null;
                bool grasp = line.Placed && line.Phase == Phase.Reel && line.GraspPrefab != null
                    && _fighters.TryGetValue(line.Partner, out var target) && target.Renderer != null && hand != null;
                if (grasp)
                {
                    if (line.Grasp == null) line.Grasp = Instantiate(line.GraspPrefab, transform);
                    var victim = _fighters[line.Partner].Renderer;
                    line.Grasp.GetComponent<PlacedTetherVisual>().Present(victim.transform.position, hand.position);
                }
                else if (line.Grasp != null)
                {
                    Destroy(line.Grasp);
                    line.Grasp = null;
                }
                Vector3? end = null;
                bool retracting = false;
                if (hand != null)
                {
                    switch (line.Phase)
                    {
                        case Phase.Flight:
                            end = line.Target;
                            break;
                        case Phase.Reel:
                            end = Hips(line.Partner);
                            break;
                        case Phase.Retract:
                            float t = (Time.time - line.RetractStart) / line.RetractSeconds;
                            if (t < 1f)
                            {
                                end = Vector3.Lerp(line.RetractFrom, hand.position, t);
                                retracting = true;
                            }
                            else line.Phase = Phase.None;
                            break;
                    }
                }
                if (retracting && !line.Placed && line.EmptyRing == null)
                    line.EmptyRing = NewRing(id);
                if (!retracting && line.EmptyRing != null)
                {
                    Destroy(line.EmptyRing);
                    line.EmptyRing = null;
                }
                if (line.EmptyRing != null && end.HasValue)
                    line.EmptyRing.transform.position = end.Value;
                line.Rope.enabled = !line.Placed && hand != null && end.HasValue;
                if (line.Rope.enabled)
                    DrawRope(line.Rope, hand!.position, end!.Value);
            }
            foreach (var (id, worn) in _worn)
                if (worn != null && Hips(id) is { } hips)
                    worn.transform.position = hips;
        }

        private Vector3? Hips(ulong id)
        {
            if (!_fighters.TryGetValue(id, out var fighter) || fighter.Renderer == null) return null;
            var bone = fighter.Renderer.ResolvePresentationBone(HipsBone);
            return bone != null ? bone.position : fighter.Renderer.transform.position;
        }

        /// <summary>The life ring of whoever threw at <paramref name="id"/> (or its own), as her slot's projectile shows it.</summary>
        private GameObject? NewRing(ulong id)
        {
            ProjectilePresentation? row = null;
            if (_wornFrom.TryGetValue(id, out var thrower) && _fighters.TryGetValue(thrower, out var from))
                row = from.Ring;
            if (row?.Prefab == null && _fighters.TryGetValue(id, out var self))
                row = self.Ring;
            foreach (var fighter in _fighters.Values)
                if (row?.Prefab == null) row = fighter.Ring;
            if (row?.Prefab == null) return null;
            var ring = Instantiate(row.Prefab, transform);
            ring.transform.localScale = Vector3.one * row.Scale;
            return ring;
        }

        private static void DrawRope(LineRenderer rope, Vector3 from, Vector3 to)
        {
            float slack = Sag * Mathf.Clamp01(Vector3.Distance(from, to) / 6f);
            for (int i = 0; i < RopePoints; i++)
            {
                float t = i / (RopePoints - 1f);
                rope.SetPosition(i, Vector3.Lerp(from, to, t) + Vector3.down * (slack * 4f * t * (1f - t)));
            }
        }

        private Line LineFor(ulong id)
        {
            if (_lines.TryGetValue(id, out var line)) return line;
            var rope = new GameObject($"TetherRope {id}").AddComponent<LineRenderer>();
            rope.transform.SetParent(transform, false);
            rope.sharedMaterial = _ropeMaterial ??= new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                color = new Color(0.93f, 0.9f, 0.82f),
            };
            rope.positionCount = RopePoints;
            rope.useWorldSpace = true;
            rope.widthMultiplier = RopeWidth;
            rope.numCapVertices = 2;
            rope.shadowCastingMode = ShadowCastingMode.Off;
            rope.receiveShadows = false;
            rope.enabled = false;
            return _lines[id] = new Line { Rope = rope };
        }

        private static bool RingInFlight(SpellResolver? resolver, ulong owner, out Vector3 position)
        {
            position = default;
            if (resolver == null) return false;
            foreach (var hb in resolver.GetActiveHitboxes())
                if (hb.OwnerId == owner && !hb.OwnerWindow && hb.AttackSlot == AbilitySlots.A)
                {
                    position = new Vector3(hb.X, hb.Y, hb.Z);
                    return true;
                }
            return false;
        }

        private static GameObject? GraspPrefab(CharacterAnimationCatalog catalog, string catchId)
        {
            if (catalog?.Presentations == null) return null;
            foreach (var binding in catalog.Presentations)
                if (binding != null && binding.SemanticId == catchId && binding.Prefab != null)
                    return binding.Prefab.GetComponent<PlacedTetherVisual>()?.DragPrefab;
            return null;
        }

        private static CookedTetherCapabilityParameters? Tether(CookedSlotDefinition? slot)
        {
            if (slot == null) return null;
            foreach (var stage in slot.Timeline.Stages)
                foreach (var operation in stage.Operations)
                    if (operation is CookedStartCapabilityOperation { Parameters: CookedTetherCapabilityParameters tether })
                        return tether;
            return null;
        }
    }
}

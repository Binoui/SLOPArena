using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.Combat;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.World
{
    /// <summary>
    /// Resolves authoritative semantic timeline events into cosmetic prefabs.
    /// It owns no gameplay state and never queries physics.
    /// </summary>
    public sealed class TimelinePresentationDispatcher
    {
        internal const int DefaultPresentationLifetimeTicks = 28;
        internal const int ExplosionPresentationLifetimeTicks = 150;

        internal static int LifetimeTicks(PresentationEventSource source)
            => source is PresentationEventSource.CapabilityExplosion or PresentationEventSource.HitContact
                or PresentationEventSource.CapabilityLaunch or PresentationEventSource.CapabilityLanding
                ? ExplosionPresentationLifetimeTicks
                : DefaultPresentationLifetimeTicks;

        internal static int LifetimeTicks(TimelinePresentationEvent presentationEvent)
            => presentationEvent.Source == PresentationEventSource.Timeline
                ? Mathf.Max(1, presentationEvent.Placement?.DurationTicks ?? DefaultPresentationLifetimeTicks)
                : LifetimeTicks(presentationEvent.Source);

        private readonly Dictionary<ulong, Entry> _entries = new();
        private readonly HashSet<string> _notInstalledWarnings = new(System.StringComparer.Ordinal);
        private readonly List<ActivePresentation> _active = new();
        private readonly HashSet<PresentationEventKey> _seenEvents = new();

        private sealed class Entry
        {
            public PlayerRenderer Renderer;
            public CharacterAnimationCatalog Catalog;
        }

        private sealed class ActivePresentation
        {
            public GameObject Instance;
            public PresentationLifetime Lifetime;
            public ulong AttackEntityId;
            public PlayerRenderer AttackOwner;
        }

        public void Register(ulong entityId, PlayerRenderer renderer, CharacterAnimationCatalog catalog)
        {
            if (renderer == null)
            {
                _entries.Remove(entityId);
                return;
            }
            _entries[entityId] = new Entry { Renderer = renderer, Catalog = catalog };
        }

        public void Tick(IReadOnlyList<TimelinePresentationEvent> events)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActivePresentation active = _active[i];
                if (active.Instance == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }
                bool ownerAvailable = active.AttackOwner != null
                    && active.AttackOwner.isActiveAndEnabled
                    && _entries.TryGetValue(active.AttackEntityId, out Entry owner)
                    && owner.Renderer == active.AttackOwner;
                if (active.Lifetime.Advance(ownerAvailable,
                        active.AttackOwner != null ? active.AttackOwner.CurrentActionState : default,
                        active.AttackOwner != null ? active.AttackOwner.CurrentAttackSequence : (byte)0,
                        active.AttackOwner != null ? active.AttackOwner.CurrentAttackElapsedTicks : 0)) continue;
                if (active.Instance != null) DestroyInstance(active.Instance);
                _active.RemoveAt(i);
            }

            if (events == null) return;
            foreach (TimelinePresentationEvent presentationEvent in events)
            {
                if (!_seenEvents.Add(presentationEvent.Key)) continue;
                if (presentationEvent.Source == PresentationEventSource.BlockContact)
                {
                    Vector3 contact = new(presentationEvent.WorldX, presentationEvent.WorldY, presentationEvent.WorldZ);
                    Vector3 normal = Vector3.zero;
                    if (_entries.TryGetValue(presentationEvent.EntityId, out Entry shieldOwner)
                        && shieldOwner.Renderer != null && shieldOwner.Renderer.CharacterDef != null)
                    {
                        PlayerRenderer fighter = shieldOwner.Renderer;
                        Vector3 center = fighter.transform.position - Vector3.up * fighter.ModelYOffset;
                        Vector3 outward = contact - center;
                        if (outward.sqrMagnitude > 0.000001f)
                        {
                            normal = outward.normalized;
                            contact = center + normal * fighter.CharacterDef.ShieldRadius;
                        }
                    }
                    GraphicHitEffect.SpawnBlock(contact, normal);
                    continue;
                }
                if (!_entries.TryGetValue(presentationEvent.EntityId, out Entry entry)) continue;
                if (entry.Renderer == null || entry.Catalog == null) continue;
                CharacterAnimationCatalog.PresentationEntry binding = FindBinding(
                    entry.Catalog.Presentations, presentationEvent.PresentationId);
                if (binding == null)
                {
                    Debug.LogError($"[Presentation] Missing binding '{presentationEvent.PresentationId}' for owner {presentationEvent.EntityId}.");
                    continue;
                }
                if (binding.Prefab == null)
                {
                    // Bound but not installed in this checkout (ADR-0038): skip the effect.
                    if (_notInstalledWarnings.Add(presentationEvent.PresentationId))
                        Debug.LogWarning($"[Presentation] '{presentationEvent.PresentationId}' is not installed in this checkout; skipping the effect.");
                    continue;
                }
                bool attachedToAttack = presentationEvent.Source == PresentationEventSource.Timeline
                    && presentationEvent.Placement?.AttachmentMode == AuthoringPresentationAttachmentMode.Bone
                    && binding.Prefab.GetComponent<AttackBoundPresentation>() != null;
                // Late confirmations must not attach an old effect to a new move or idle pose.
                if (attachedToAttack && (!entry.Renderer.isActiveAndEnabled
                    || !PresentationLifetime.IsCurrentAttack(entry.Renderer.CurrentActionState,
                        entry.Renderer.CurrentAttackSequence, presentationEvent.AttackSequence)))
                    continue;

                GameObject instance = PresentationPlacementResolver.Instantiate(
                    binding,
                    presentationEvent.Placement,
                    entry.Renderer,
                    entry.Renderer.transform,
                    new Vector3(presentationEvent.WorldX, presentationEvent.WorldY, presentationEvent.WorldZ),
                    Quaternion.Euler(0f, presentationEvent.WorldYaw * Mathf.Rad2Deg, 0f));
                if (instance == null) continue;
                _active.Add(new ActivePresentation
                {
                    Instance = instance,
                    Lifetime = new PresentationLifetime(LifetimeTicks(presentationEvent), attachedToAttack,
                        presentationEvent.AttackSequence, entry.Renderer.CurrentAttackElapsedTicks),
                    AttackEntityId = presentationEvent.EntityId,
                    AttackOwner = attachedToAttack ? entry.Renderer : null,
                });
            }
        }

        public void Clear()
        {
            foreach (ActivePresentation active in _active)
                if (active.Instance != null) DestroyInstance(active.Instance);
            _active.Clear();
            _entries.Clear();
            _seenEvents.Clear();
        }

        private static CharacterAnimationCatalog.PresentationEntry FindBinding(
            CharacterAnimationCatalog.PresentationEntry[] bindings, string semanticId)
        {
            if (bindings == null) return null;
            foreach (CharacterAnimationCatalog.PresentationEntry binding in bindings)
                if (binding != null && binding.SemanticId == semanticId)
                    return binding;
            return null;
        }
        private static void DestroyInstance(GameObject instance)
        {
            if (Application.isPlaying)
                Object.Destroy(instance);
            else
                Object.DestroyImmediate(instance);
        }
    }
}

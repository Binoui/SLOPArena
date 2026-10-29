using System;
using System.Linq;
using System.Collections.Generic;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    /// <summary>The host is a cosmetic rig, never a fighter or a simulation entity.</summary>
    public sealed class MankiHostPresenter : IDisposable
    {
        public Transform Root { get; }
        public Transform Hand { get; }
        public CharacterAnimationCatalog Catalog { get; }
        public ProjectilePresentation Bomb { get; }
        private readonly AnimancerComponent _animancer;
        private readonly AnimationClip _idle, _throw, _slam, _call, _laugh, _hype;
        private readonly Dictionary<MayhemEventKey, MankiMayhemEvent> _reactions = new();
        public string Reaction { get; private set; } = "idle";
        private readonly GameObject _held;
        private readonly Vector3 _rigPosition;
        private readonly Transform _rig;

        public MankiHostPresenter(Transform parent, MatchContentEntry manki, MankiMayhemDefinition stage)
        {
            if (!CookedCharacterClientAssetResolver.TryResolve(manki, out var catalog, out var rig, out var error))
                throw new InvalidOperationException($"Mayhem host assets: {error}");
            Root = new GameObject("Manki Host (presentation only)").transform;
            Root.SetParent(parent, false);
            Root.position = new Vector3(stage.HostOrigin.X, stage.HostOrigin.Y, stage.HostOrigin.Z);
            Root.rotation = Quaternion.Euler(0f, 180f, 0f);
            Root.localScale = Vector3.one * 3.5f;
            var model = Object.Instantiate(rig, Root);
            _rig = model.transform;
            _rig.localPosition = Vector3.zero;
            _rig.localRotation = Quaternion.identity;
            _rig.localScale = Vector3.one * manki.Definition.VisualScale;
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
            var animator = model.GetComponentInChildren<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animancer = animator.GetComponent<AnimancerComponent>() ?? animator.gameObject.AddComponent<AnimancerComponent>();
            _animancer.Animator = animator;
            _idle = catalog.Animations.FirstOrDefault(a => a.SemanticId == "anim.manki.idle")?.Clip;
            _throw = catalog.Animations.FirstOrDefault(a => a.SemanticId == "anim.manki.ga")?.Clip;
            _slam = catalog.Animations.FirstOrDefault(a => a.SemanticId == "anim.manki.g4")?.Clip;
            _call = catalog.Animations.First(a => a.SemanticId == "anim.manki.g1").Clip;
            _laugh = catalog.Animations.First(a => a.SemanticId == manki.Definition.MayhemLaughAnim).Clip;
            _hype = catalog.Animations.First(a => a.SemanticId == "anim.manki.a3").Clip;
            if (_slam == null) throw new InvalidOperationException("Mayhem host needs the installed Head Slam clip.");
            if (_idle == null || _throw == null) throw new InvalidOperationException("Mayhem host needs Manki's installed idle and Round Bomb throw clips.");
            var idle = _animancer.Play(_idle);
            idle.Speed = 0;
            _animancer.Evaluate(0f);
            var renderers = model.GetComponentsInChildren<Renderer>();
            float height = renderers.Max(r => r.bounds.max.y) - renderers.Min(r => r.bounds.min.y);
            Root.localScale *= 6.2f / height;
            float floor = renderers.Min(r => r.bounds.min.y);
            _rig.position += Vector3.up * (Root.position.y - floor);
            _rigPosition = _rig.localPosition;
            Hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Bomb = catalog.PresentationProfile.FindProjectile(AbilitySlots.A, false);
            if (Bomb?.Prefab == null || Hand == null) throw new InvalidOperationException("Mayhem host needs Manki's Round Bomb prop and right hand.");
            _held = Object.Instantiate(Bomb.Prefab);
            _held.name = "Host held Round Bomb";
            _held.transform.localScale = Vector3.one * Bomb.Scale;
            _held.SetActive(false);
            Catalog = Object.Instantiate(catalog);
            Catalog.hideFlags = HideFlags.DontSave;
            Catalog.Presentations = new[] { new CharacterAnimationCatalog.PresentationEntry
            {
                SemanticId = "mayhem.scatter.blast",
                Prefab = catalog.Presentations.First(p => p.SemanticId == "presentation.manki.round-bomb.explosion").Prefab,
            } };
        }

        public void Present(ServerSimulation simulation, IReadOnlyList<MankiMayhemEvent> events = null,
            IReadOnlyList<MankiMayhemEvent> retracted = null)
        {
            if (simulation.MankiMayhem is not MankiMayhemState director) return;
            if (retracted != null)
                foreach (var e in retracted) _reactions.Remove(e.Key);
            foreach (var e in events ?? simulation.LastTickMayhemEvents)
                if (e.Kind is MankiMayhemEventKind.Call or MankiMayhemEventKind.Bench or MankiMayhemEventKind.End
                    || e.Kind == MankiMayhemEventKind.Score && e.Points > MankiMayhemTuning.SurvivalPoints)
                    _reactions[e.Key] = e;
            foreach (var pair in _reactions.ToArray())
                if (pair.Value.RoundIndex != director.RoundIndex || simulation.CurrentTick > pair.Value.MatchTick + 150
                    || pair.Value.MatchTick > simulation.CurrentTick) _reactions.Remove(pair.Key);

            _held.SetActive(false);
            AnimationClip clip = _idle;
            float time = simulation.CurrentTick / 60f % _idle.length;
            Reaction = "idle";
            var reaction = _reactions.Values.OrderByDescending(e => e.Kind == MankiMayhemEventKind.End ? 3
                : e.Kind == MankiMayhemEventKind.Score ? 2 : e.Kind == MankiMayhemEventKind.Bench ? 1 : 0)
                .ThenByDescending(e => e.MatchTick).FirstOrDefault();
            long age = (long)simulation.CurrentTick - reaction.MatchTick;
            var bench = _reactions.Values.Where(e => e.Kind == MankiMayhemEventKind.Bench).OrderByDescending(e => e.MatchTick).FirstOrDefault();
            long benchAge = (long)simulation.CurrentTick - bench.MatchTick;
            if (director.Ended)
            {
                clip = _hype; time = _hype.length * .28f; Reaction = "finale";
            }
            else if (bench.MatchTick > 0 && benchAge < 150 && (reaction.Kind != MankiMayhemEventKind.Score || age < 36))
            {
                clip = _laugh; time = benchAge / 60f % clip.length; Reaction = "laugh";
            }
            else if (reaction.Kind == MankiMayhemEventKind.Score && reaction.MatchTick > 0 && age < 120)
            {
                clip = _hype; time = _hype.length * Mathf.Lerp(0f, .28f, Mathf.Clamp01(age / 24f)); Reaction = "hype";
            }
            else if (director.Phase == MankiMayhemPhase.Call && director.PhaseTick < (director.Card == MankiMayhemCard.BreakOut ? 12 : 42))
            {
                clip = _call; time = _call.length * Mathf.Clamp01(director.PhaseTick / (director.Card == MankiMayhemCard.BreakOut ? 12f : 42f)); Reaction = "call";
            }

            if (!director.Ended && director.Phase is MankiMayhemPhase.Call or MankiMayhemPhase.Pattern or MankiMayhemPhase.Settle)
            {
                if (director.Card == MankiMayhemCard.Jump)
                {
                    var nearest = simulation.MayhemSlams.OrderBy(s => Math.Abs((long)s.Tick - simulation.CurrentTick)).FirstOrDefault();
                    long slamAge = (long)simulation.CurrentTick - nearest.Tick;
                    if (nearest.Tick > 0 && slamAge >= -MankiMayhemTuning.WindUpTicks && slamAge <= 36)
                    {
                        clip = _slam;
                        // Head Slam's floor contact is at 40%; Shared's Slam is the contact clock.
                        time = slamAge <= 0 ? _slam.length * .4f * (slamAge + MankiMayhemTuning.WindUpTicks) / MankiMayhemTuning.WindUpTicks
                            : _slam.length * Mathf.Lerp(.4f, 1f, slamAge / 36f);
                        Reaction = "slam";
                    }
                }
                else
                {
                    var rings = simulation.MayhemLandingRings;
                    for (int i = 0; i < rings.Count; i++)
                    {
                        var ring = rings[i];
                        uint wind = i == 0 ? ring.LaunchTick - MankiMayhemTuning.WindUpTicks : rings[i - 1].LaunchTick;
                        if (i == 0 && director.Card == MankiMayhemCard.BreakOut) wind += 12;
                        long throwAge = (long)simulation.CurrentTick - ring.LaunchTick;
                        if (simulation.CurrentTick < wind || throwAge > 18) continue;
                        if (i + 1 < rings.Count && simulation.CurrentTick >= ring.LaunchTick) continue;
                        clip = _throw;
                        time = throwAge <= 0 ? _throw.length * .38f * Mathf.Clamp01((simulation.CurrentTick - wind) / (float)Math.Max(1u, ring.LaunchTick - wind))
                            : _throw.length * Mathf.Lerp(.38f, 1f, throwAge / 18f);
                        _held.SetActive(throwAge < 0);
                        Reaction = director.Card == MankiMayhemCard.BreakOut ? "lob" : "throw";
                        break;
                    }
                }
            }
            var pose = _animancer.Play(clip); pose.Speed = 0f; pose.Time = time;
            _animancer.Evaluate(0f); _rig.localPosition = _rigPosition;
            _held.transform.position = Hand.position;
        }

        public void Dispose()
        {
            Object.Destroy(_held);
            Object.Destroy(Root.gameObject);
            Object.Destroy(Catalog);
        }
    }
}

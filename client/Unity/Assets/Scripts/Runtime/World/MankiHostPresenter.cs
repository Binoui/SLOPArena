using System;
using System.Linq;
using System.Collections.Generic;
using Animancer;
using SlopArena.Client.Animation;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
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
        private readonly Dictionary<MayhemEventKey, MayhemEvent> _reactions = new();
        public string Reaction { get; private set; } = "idle";
        private readonly GameObject _held;
        private readonly Vector3 _rigPosition;
        private readonly Transform _rig;
        private readonly Vector3 _boothPosition;
        private Vector3 _deckPosition;

        public MankiHostPresenter(Transform parent, MatchContentEntry manki, MankiMayhemDefinition stage)
        {
            if (!CookedCharacterClientAssetResolver.TryResolve(manki, out var catalog, out var rig, out var error))
                throw new InvalidOperationException($"Mayhem host assets: {error}");
            Root = new GameObject("Manki Host (presentation only)").transform;
            Root.SetParent(parent, false);
            Root.position = new Vector3(stage.HostOrigin.X, stage.HostOrigin.Y, stage.HostOrigin.Z);
            _boothPosition = Root.position;
            _deckPosition = new Vector3((stage.MinX + stage.MaxX) * .5f, stage.DeckTop + GetHimTuning.HalfHeight, (stage.MinZ + stage.MaxZ) * .5f);
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
            _slam = catalog.Animations.FirstOrDefault(a => a.SemanticId == "anim.manki.g3")?.Clip;
            _call = catalog.Animations.First(a => a.SemanticId == "anim.manki.g1").Clip;
            _laugh = catalog.Animations.First(a => a.SemanticId == manki.Definition.MayhemLaughAnim).Clip;
            _hype = catalog.Animations.First(a => a.SemanticId == "anim.manki.a2").Clip;
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
                SemanticId = "mayhem.bomb.blast",
                Prefab = catalog.Presentations.First(p => p.SemanticId == "presentation.manki.round-bomb.explosion").Prefab,
            } };
        }

        public void Present(ServerSimulation simulation, IReadOnlyList<MayhemEvent> events = null,
            IReadOnlyList<MayhemEvent> retracted = null)
        {
            var view = simulation.MayhemView; if (view == null) return;
            if (retracted != null) foreach (var e in retracted) _reactions.Remove(e.Key);
            foreach (var e in events ?? simulation.LastTickMayhemEvents) _reactions[e.Key] = e;
            foreach (var pair in _reactions.ToArray())
                if (pair.Value.MatchTick > view.Tick || view.Tick-pair.Value.MatchTick>120) _reactions.Remove(pair.Key);
            AnimationClip clip = _idle;
            float time = view.Tick/60f % _idle.length;
            Reaction = "idle"; _held.SetActive(false);
            var reaction = _reactions.Values.OrderByDescending(e=>e.MatchTick).FirstOrDefault();
            float age = (view.Tick-reaction.MatchTick)/60f;
            if (view.Cursor.Phase == MayhemPhase.Ended || view.Cursor.Phase == MayhemPhase.SpeedUp)
            { clip=_hype; time=age%clip.length; Reaction=view.Cursor.Phase==MayhemPhase.Ended?"finale":"hype"; }
            else if (view.Cursor.Phase == MayhemPhase.Call)
            { clip=_call; time=Mathf.Min(_call.length,(view.Tick-view.Cursor.PhaseStart)/60f); Reaction="call"; }
            else if (reaction.Kind == MayhemEventKind.Fail)
            { clip=_laugh; time=age%clip.length; Reaction="laugh"; }
            var slam = view.Scene.Rings.FirstOrDefault(r => (long)view.Tick-r.SlamTick>=-30 && (long)view.Tick-r.SlamTick<=36);
            if (slam.SlamTick>0 && view.Cursor.Phase==MayhemPhase.Play)
            {
                clip=_slam; float ticks=(long)view.Tick-slam.SlamTick;
                time=clip.length*(ticks<0?.4f*(ticks+30)/30f:Mathf.Lerp(.4f,1f,ticks/36f)); Reaction="slam";
            }
            var bomb = view.Scene.Bombs.FirstOrDefault(r => !r.Exploded && (long)view.Tick-r.LaunchTick>=-30 && (long)view.Tick-r.LaunchTick<=18);
            if (bomb.LaunchTick>0 && view.Cursor.Gameplay)
            {
                clip=_throw; float ticks=(long)view.Tick-bomb.LaunchTick;
                time=clip.length*(ticks<0?.38f*(ticks+30)/30f:Mathf.Lerp(.38f,1,ticks/18f)); Reaction="throw"; _held.SetActive(ticks<0);
            }
            var target = view.Scene.Targets.FirstOrDefault(t => t.Kind == MayhemTargetKind.DizzyHost);
            bool finale = view.Channel?.Id == MayhemChannels.GetHim;
            bool onDeck = finale && (view.Cursor.Phase == MayhemPhase.Play || view.Cursor.Phase == MayhemPhase.Verdict);
            Root.position = _boothPosition; Root.rotation = Quaternion.Euler(0, 180, 0);
            if (onDeck)
            {
                if (target.EntityId != 0) _deckPosition = new Vector3(target.Center.X, target.Center.Y, target.Center.Z);
                Root.position = _deckPosition;
                if (target.EntityId != 0 && view.Tick < target.ActiveUntil)
                {
                    float fall = Mathf.Clamp01(((long)view.Tick - target.ActiveFrom) / 18f);
                    Root.position = Vector3.Lerp(_boothPosition, _deckPosition, fall) + Vector3.up * Mathf.Sin(fall * Mathf.PI) * 2;
                    Root.rotation = Quaternion.Euler(0, 180, 75 * fall);
                    clip = _idle; time = 0; Reaction = "dizzy"; _held.SetActive(false);
                }
            }
            if (view.Cursor.Phase == MayhemPhase.Ended)
            {
                float rise = Mathf.Clamp01(((long)view.Tick - view.Cursor.PhaseStart) / 24f);
                Root.position = Vector3.Lerp(_deckPosition, _boothPosition, rise);
            }
            var pose = _animancer.Play(clip); pose.Speed = 0f; pose.Time = time;
            _animancer.Evaluate(0f); _rig.localPosition = _rigPosition;
            _held.transform.position = Hand.position;
        }

        /// <summary>A Replay seek: reactions older than its warmup are not known, so none carry over.</summary>
        public void Reset() { _reactions.Clear(); Root.position = _boothPosition; Root.rotation = Quaternion.Euler(0, 180, 0); }

        public void Dispose()
        {
            Object.Destroy(_held);
            Object.Destroy(Root.gameObject);
            Object.Destroy(Catalog);
        }
    }
}

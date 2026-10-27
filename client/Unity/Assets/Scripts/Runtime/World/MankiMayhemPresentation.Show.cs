using System;
using System.Collections.Generic;
using System.Linq;
using Animancer;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private sealed class Crt
        {
            public Transform Root, Raster;
            public TextMesh Title, Subtitle, Header;
        }
        private sealed class BoothSeat
        {
            public GameObject Rig;
            public AnimancerComponent Animator;
            public AnimationClip Idle;
            public TextMesh Label;
            public bool WasActive;
            public Vector3 Position;
        }
        private readonly List<Crt> _screens = new();
        private readonly Dictionary<ulong, BoothSeat> _bench = new();
        private readonly Dictionary<MayhemEventKey, GameObject> _showSounds = new();
        private Font _showFont;
        private Material _screenInk;
        private readonly AudioClip _callSting = Resources.Load<AudioClip>("Audio/SFX/CoursePier/course_bell");
        private readonly AudioClip _slamSting = Resources.Load<AudioClip>("Audio/SFX/Bonk/impact_heavy");
        private readonly AudioClip _bonusSting = Resources.Load<AudioClip>("Audio/SFX/CoursePier/course_all_clear");

        private TextMesh Lettering(Transform parent, string name, Vector3 position, float size, Color color)
        {
            var text = new GameObject(name).AddComponent<TextMesh>();
            text.transform.SetParent(parent, false); text.transform.localPosition = position;
            text.font = _showFont; text.GetComponent<MeshRenderer>().sharedMaterial = _showFont.material;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.fontSize = 100; text.characterSize = size * .1f; text.fontStyle = FontStyle.Bold;
            text.color = color; text.richText = false;
            return text;
        }

        private void CreateShow(IReadOnlyList<HUDManager.HudPlayer> players)
        {
            _showFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _screenInk = Material(new Color(.025f, .035f, .03f));
            foreach (string name in new[] { "Mayhem CRT Left", "Mayhem CRT Right" })
            {
                var marker = _root.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>())
                    .SingleOrDefault(t => t.name == name);
                if (marker == null) throw new InvalidOperationException($"Mayhem needs the named stage surface '{name}'. Reapply presentation.json.");
                var root = new GameObject(name + " live feed").transform; root.SetParent(_root, false);
                root.SetPositionAndRotation(marker.position, marker.rotation);
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(face.GetComponent<Collider>()); face.transform.SetParent(root, false);
                face.transform.localScale = new Vector3(marker.lossyScale.x, marker.lossyScale.y, 1f);
                face.GetComponent<Renderer>().sharedMaterial = _screenInk;
                var crt = new Crt { Root = root };
                crt.Header = Lettering(root, "Broadcast strap", new Vector3(0, 2.6f, -.04f), .48f, new Color(1f, .48f, .12f));
                crt.Title = Lettering(root, "Call card", new Vector3(0, 1.15f, -.06f), 1.15f, new Color(1f, .78f, .17f));
                crt.Title.transform.localRotation = Quaternion.Euler(0, 0, -3f);
                crt.Subtitle = Lettering(root, "Call subtitle", new Vector3(0, -1.85f, -.06f), .60f, new Color(.94f, .9f, .72f));
                for (int i = 0; i < 22; i++)
                {
                    var stripe = Line(root, "CRT scanline", _ink, .018f); stripe.positionCount = 2;
                    float y = -3f + i * .28f;
                    stripe.SetPositions(new[] { new Vector3(-5.1f, y, -.08f), new Vector3(5.1f, y, -.08f) });
                }
                crt.Raster = Line(root, "Travelling raster", _gold, .025f).transform;
                var raster = crt.Raster.GetComponent<LineRenderer>(); raster.positionCount = 2;
                raster.SetPositions(new[] { new Vector3(-5.1f, 0f, -.1f), new Vector3(5.1f, 0f, -.1f) });
                _screens.Add(crt);
            }
            int seatIndex = 0;
            foreach (var identity in players)
            {
                var catalog = _fighters[identity.EntityId].PresentationCatalog;
                var rig = Object.Instantiate(catalog.Rig, _root);
                rig.name = $"Booth P{identity.PlayerNumber} waiting fighter";
                float x = new[] { -7.8f, -5.1f, 5.1f, 7.8f }[seatIndex++];
                rig.transform.SetPositionAndRotation(Host.Root.position + new Vector3(x, .2f, -3.25f), Quaternion.Euler(0, 180, 0));
                foreach (var collider in rig.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
                var animator = rig.GetComponentInChildren<Animator>(); animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var animancer = animator.GetComponent<AnimancerComponent>();
                if (animancer == null) animancer = animator.gameObject.AddComponent<AnimancerComponent>();
                animancer.Animator = animator;
                var idle = catalog.Animations.First(a => a.SemanticId.EndsWith(".idle", StringComparison.Ordinal)).Clip;
                var pose = animancer.Play(idle); pose.Speed = 0; animancer.Evaluate(0);
                var renderers = rig.GetComponentsInChildren<Renderer>();
                float height = renderers.Max(r => r.bounds.max.y) - renderers.Min(r => r.bounds.min.y);
                rig.transform.localScale *= 3.2f / height;
                rig.transform.position += Vector3.up * (Host.Root.position.y + .2f - renderers.Min(r => r.bounds.min.y));
                var label = Lettering(_root, $"Booth P{identity.PlayerNumber} OUT", Host.Root.position + new Vector3(x, 3.9f, -3.25f),
                    .65f, SeatColours.For(identity.PlayerNumber));
                label.transform.position = Host.Root.position + new Vector3(x, 3.9f, -3.25f);
                label.text = $"P{identity.PlayerNumber} · OUT";
                var seat = new BoothSeat { Rig = rig, Animator = animancer, Idle = idle, Label = label,
                    WasActive = _fighters[identity.EntityId].gameObject.activeSelf, Position = rig.transform.position };
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube); pad.name = $"Booth P{identity.PlayerNumber} bench pad";
                Object.Destroy(pad.GetComponent<Collider>()); pad.transform.SetParent(_root, false);
                pad.transform.position = Host.Root.position + new Vector3(x, .1f, -3.25f);
                pad.transform.localScale = new Vector3(2.2f, .18f, 1.15f); pad.GetComponent<Renderer>().sharedMaterial = _gold;
                rig.SetActive(false); label.gameObject.SetActive(false); _bench.Add(identity.EntityId, seat);
            }
        }

        private void ShowEvents(IReadOnlyList<MankiMayhemEvent> events, IReadOnlyList<MankiMayhemEvent> retracted)
        {
            if (retracted != null)
                foreach (var e in retracted)
                    if (_showSounds.Remove(e.Key, out var sound)) Object.Destroy(sound);
            foreach (var e in events)
            {
                AudioClip clip = e.Kind == MankiMayhemEventKind.Call ? _callSting
                    : e.Kind == MankiMayhemEventKind.Slam ? _slamSting
                    : e.Kind == MankiMayhemEventKind.End || e.Kind == MankiMayhemEventKind.Score && e.Points > MankiMayhemTuning.SurvivalPoints ? _bonusSting : null;
                if (clip == null || _showSounds.ContainsKey(e.Key)) continue;
                var sound = new GameObject("Mayhem show sting"); sound.transform.SetParent(_root, false);
                var audio = sound.AddComponent<AudioSource>(); audio.clip = clip; audio.volume = .32f; audio.spatialBlend = 0;
                audio.outputAudioMixerGroup = ClientSettingsService.Instance.FindBus("SFX");
                audio.Play(); _showSounds.Add(e.Key, sound);
            }
            foreach (var pair in _showSounds.ToArray())
                if (_sim.CurrentTick > pair.Key.MatchTick + 180 || _sim.CurrentTick < pair.Key.MatchTick)
                { Object.Destroy(pair.Value); _showSounds.Remove(pair.Key); }
        }

        private void UpdateShow()
        {
            if (_sim.MankiMayhem is not MankiMayhemState state) return;
            foreach (var player in _sim.MayhemPlayers)
            {
                var seat = _bench[player.EntityId];
                bool waiting = player.Benched && !player.Withdrawn;
                seat.Rig.SetActive(waiting); seat.Label.gameObject.SetActive(waiting);
                _fighters[player.EntityId].gameObject.SetActive(!player.Benched && seat.WasActive);
                if (waiting)
                {
                    var idle = seat.Animator.Play(seat.Idle); idle.Speed = 0;
                    idle.Time = _sim.CurrentTick / 60f % seat.Idle.length; seat.Animator.Evaluate(0);
                    seat.Rig.transform.position = seat.Position;
                }
            }
            bool call = state.Phase == MankiMayhemPhase.Call;
            string scores = string.Join("\n", _sim.MayhemPlayers.OrderByDescending(p => p.Score).Select(p =>
                $"{_view.Find(p.EntityId).Label}   {p.Score:00}{(p.Benched ? "  OUT" : "")}"));
            foreach (var crt in _screens)
            {
                crt.Header.text = state.Ended ? "THAT'S THE SHOW!" : $"MANKI MAYHEM  /  {state.RoundIndex + 1:00}";
                crt.Title.transform.localPosition = new Vector3(0, call ? 1.15f : .45f, -.06f);
                crt.Title.text = call ? MankiMayhemView.CardTitle(state.Card) : scores;
                crt.Title.characterSize = (call ? (state.Card == MankiMayhemCard.BreakOut ? 1.04f : 1.3f) : .65f) * .1f;
                crt.Subtitle.text = call ? MankiMayhemView.CardSubtitle(state.Card).Replace(" · ", "\n")
                    : state.Ended ? "THANKS FOR SURVIVING" : "LIVE SCORES / SURVIVE TO SCORE";
                float flicker = ClientSettingsService.Instance.ReducedFlashing ? 1f : .94f + .06f * Mathf.Sin(_sim.CurrentTick * 1.7f);
                crt.Title.color = new Color(flicker, .78f * flicker, .17f * flicker);
                crt.Raster.gameObject.SetActive(!ClientSettingsService.Instance.ReducedFlashing);
                crt.Raster.localPosition = Vector3.up * (3f - _sim.CurrentTick % 180 / 30f);
            }
        }

        private void DisposeShow()
        {
            foreach (var pair in _bench)
                if (_fighters[pair.Key] != null) _fighters[pair.Key].gameObject.SetActive(pair.Value.WasActive);
            Object.Destroy(_screenInk);
        }
    }
}

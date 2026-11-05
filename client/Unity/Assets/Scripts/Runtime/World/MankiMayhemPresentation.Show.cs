using System;
using System.Collections.Generic;
using System.Linq;
using Animancer;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private sealed class Crt
        {
            public Transform Root, Raster;
            public Renderer Guest;
            public Material GuestMaterial;
            public TextMesh Title, Subtitle, Header;
        }
        private readonly List<Crt> _screens = new();
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
                var guest = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(guest.GetComponent<Collider>());
                guest.transform.SetParent(root,false); guest.transform.localPosition = new Vector3(-4f,1f,-.12f);
                guest.transform.localScale = new Vector3(1.5f,1.6f,1);
                crt.Guest=guest.GetComponent<Renderer>(); crt.GuestMaterial=Material(Color.white); crt.Guest.sharedMaterial=crt.GuestMaterial;
                _screens.Add(crt);
            }
        }

        private void ShowEvents(IReadOnlyList<MayhemEvent> events, IReadOnlyList<MayhemEvent> retracted)
        {
            if (retracted != null)
                foreach (var e in retracted)
                    if (_showSounds.Remove(e.Key, out var sound)) Object.Destroy(sound);
            foreach (var e in events)
            {
                AudioClip clip = e.Kind == MayhemEventKind.Call ? _callSting
                    : e.Kind == MayhemEventKind.WatchStart ? Resources.Load<AudioClip>("Audio/SFX/Bashie/bashie_whistle_tweet")
                    : e.Kind == MayhemEventKind.Slam ? _slamSting
                    : e.Kind == MayhemEventKind.Ended || e.Kind == MayhemEventKind.SpeedUp ? _bonusSting : null;
                if (clip == null || _showSounds.ContainsKey(e.Key) || !PresentationOutput.TryPlay()) continue;
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
            var view = _sim.MayhemView; if (view == null) return;
            foreach (var player in view.Contestants)
            {
                var fighter = _fighters[player.EntityId];
                fighter.gameObject.SetActive(!player.Withdrawn);
                foreach(var child in fighter.GetComponentsInChildren<Transform>())
                    if(child.name=="Jetpack fuel") child.gameObject.SetActive(false);
                if (!_originalScale.TryGetValue(player.EntityId,out var scale)) _originalScale[player.EntityId] = scale = fighter.transform.localScale;
                fighter.transform.localScale = player.Failure?.Style == SplatStyle.Flattened ? Vector3.Scale(scale,new Vector3(1.5f,.2f,1.5f)) : scale;
                foreach (var renderer in fighter.GetComponentsInChildren<Renderer>())
                {
                    var block = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_BaseColor",player.Failure?.Style == SplatStyle.Toasted ? new Color(.2f,.15f,.12f) : Color.white);
                    renderer.SetPropertyBlock(block);
                }
                for (int i=0;i<3;i++)
                {
                    var puff = Prop($"Splat {player.EntityId}/{i}",PrimitiveType.Sphere,_gold);
                    bool failed = player.Failure != null;
                    puff.gameObject.SetActive(failed);
                    if (!failed) continue;
                    var failure = player.Failure.Value;
                    float age = (view.Tick-failure.Tick)/60f;
                    bool dizzy = failure.Style == SplatStyle.Dizzy;
                    float angle = age*4 + i*Mathf.PI*2/3;
                    puff.position = fighter.transform.position + new Vector3(Mathf.Cos(angle)*.6f,dizzy?2f:.3f+(age%1f),Mathf.Sin(angle)*.6f);
                    puff.localScale = Vector3.one*(dizzy?.32f:.35f);
                    puff.GetComponent<Renderer>().enabled=!dizzy;
                    var colour=new MaterialPropertyBlock(); colour.SetColor("_BaseColor",failure.Style==SplatStyle.Toasted?new Color(.22f,.2f,.18f):new Color(.65f,.5f,.25f)); puff.GetComponent<Renderer>().SetPropertyBlock(colour);
                    if(dizzy && puff.childCount==0)
                    {
                        var star=Line(puff,"Dizzy star",_gold,.12f); star.positionCount=11;
                        for(int j=0;j<=10;j++) { float a=j*Mathf.PI/5; float radius=j%2==0?1f:.4f; star.SetPosition(j,new Vector3(Mathf.Sin(a)*radius,Mathf.Cos(a)*radius,0)); }
                    }
                    if(puff.childCount>0) puff.GetChild(0).gameObject.SetActive(dizzy);
                }
            }
            var c = view.Cursor;
            UISFX.SetMayhemMusicAct(c.Act);
            string title = c.Phase == MayhemPhase.Intro ? "YOU'RE ON AIR!" : c.Phase == MayhemPhase.SpeedUp ? "SPEED UP!" : c.Phase == MayhemPhase.Ended ? "THAT'S THE SHOW!" : MankiMayhemView.CardTitle(view.Channel);
            string subtitle = view.Scene.Watches.Count>0 ? MankiMayhemView.WatchText(view) : MankiMayhemView.CardSubtitle(view.Channel);
            foreach (var crt in _screens)
            {
                var guest = view.Channel?.Id == MayhemChannels.NoRunning ? CharacterClass.Bashie : view.Channel?.Id == MayhemChannels.BreakBoards ? CharacterClass.FightGuy : CharacterClass.None;
                crt.Guest.gameObject.SetActive(guest != CharacterClass.None);
                if(guest != CharacterClass.None) crt.GuestMaterial.SetTexture("_BaseMap",Resources.Load<Texture2D>($"UI/Portraits/{guest}"));
                crt.Header.text = $"MANKI MAYHEM / ACT {c.Act+1} / {Math.Max(0,c.Episode+1):00}";
                crt.Title.text = title;
                crt.Title.characterSize = (title.Length>14?.62f:1.1f)*.1f;
                crt.Subtitle.text = subtitle.Replace(". ",".\n") + "\n" + ((Label)HUDManager.MayhemChip(SlopArena.Client.Input.HumanInputActions.LastUsedGamepad?"MoveStick":"MoveUp","Move")).text + "\n" + ((Label)HUDManager.MayhemChip("Jump","Jump")).text + "   " + ((Label)HUDManager.MayhemChip("Normal1","Hit")).text;
                crt.Subtitle.characterSize = .045f;
                crt.Title.color = view.Scene.Watches.Count>0 ? (subtitle=="NOT LOOKING"?Color.green:Color.red) : MayhemChannelLooks.For(view.Channel).Accent;
                crt.Raster.gameObject.SetActive(!ClientSettingsService.Instance.ReducedFlashing);
                crt.Raster.localPosition = Vector3.up*(3f-view.Tick%180/30f);
            }
        }
        private readonly Dictionary<ulong,Vector3> _originalScale = new();

        private void DisposeShow()
        {
            UISFX.SetMayhemMusicAct(0);
            foreach(var crt in _screens) Object.Destroy(crt.GuestMaterial);
            Object.Destroy(_screenInk);
        }
    }
}

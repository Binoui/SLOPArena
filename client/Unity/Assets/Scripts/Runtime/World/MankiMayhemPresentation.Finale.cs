using System.Collections.Generic;
using System.Linq;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private sealed class PotatoProp
        {
            public Transform Root, Ember;
            public LineRenderer Fuse;
            public TextMesh Label;
            public CarryCue Cue;
            public uint RetiredAt;
            public bool Retired;
        }
        private readonly Dictionary<int, PotatoProp> _potatoes = new();
        private readonly Dictionary<int, Transform> _barrages = new();
        private Transform _hostStars;
        private readonly Dictionary<byte, Transform> _returnLocks = new();

        private void UpdateFinalChannels(MayhemShowView view)
        {
            var target = view.Scene.Targets.FirstOrDefault(t => t.Kind == MayhemTargetKind.DizzyHost);
            if (target.EntityId != 0 && _hostStars == null)
            {
                _hostStars = PropRoot("Dizzy host stars");
                for (int i = 0; i < 5; i++)
                {
                    var star = Line(_hostStars, "Cardboard star", _gold, .14f); star.positionCount = 11;
                    for (int j = 0; j <= 10; j++)
                    {
                        float a = j * Mathf.PI / 5, r = j % 2 == 0 ? .45f : .18f;
                        star.SetPosition(j, new Vector3(Mathf.Sin(a) * r, Mathf.Cos(a) * r, 0));
                    }
                }
            }
            if (_hostStars != null)
            {
                _hostStars.gameObject.SetActive(target.EntityId != 0 && view.Cursor.Gameplay
                    && view.Tick >= target.ActiveFrom && view.Tick < target.ActiveUntil);
                _hostStars.position = Host.Root.position + Vector3.up * 2.4f;
                for (int i = 0; i < _hostStars.childCount; i++)
                {
                    float a = view.Tick / 18f + i * Mathf.PI * 2 / 5;
                    _hostStars.GetChild(i).localPosition = new Vector3(Mathf.Cos(a) * 2, Mathf.Sin(a * 2) * .25f, Mathf.Sin(a) * 1.2f);
                }
            }
            var live = new HashSet<int>();
            foreach (var cue in view.Scene.Carries)
            {
                int key = cue.Lane * 65536 + cue.Ordinal; live.Add(key);
                if (!_potatoes.TryGetValue(key, out var prop))
                {
                    var root = PropRoot("Carried cardboard potato");
                    var shell = Object.Instantiate(_bombTemplateHolder.GetChild(0), root, false);
                    shell.gameObject.SetActive(true); shell.localScale = Vector3.one * 1.5f;
                    prop = new PotatoProp { Root = root };
                    prop.Fuse = Line(root, "Remaining fuse", _gold, .09f);
                    prop.Fuse.positionCount = 2;
                    prop.Ember = DeckObject(root, "Live fuse ember", _sphereMesh, _hot, Vector3.zero, Vector3.one * .18f);
                    prop.Label = Lettering(root, "Potato fuse clock", new Vector3(0, 1.6f, -.1f), .65f, Color.white);
                    _potatoes.Add(key, prop);
                }
                prop.Cue = cue; prop.Retired = false;
                prop.Root.gameObject.SetActive(view.Cursor.Gameplay && view.Tick < cue.FuseEnd);
                var holder = view.Contestants.FirstOrDefault(c => c.Seat == cue.HolderSeat);
                if (holder != null && _fighters.TryGetValue(holder.EntityId, out var fighter))
                    prop.Root.position = fighter.transform.position + Vector3.up * 2.6f;
                float left = Mathf.Max(0, (long)cue.FuseEnd - view.Tick);
                float length = Mathf.Clamp(left / 180f, .08f, 1);
                prop.Fuse.SetPosition(0, Vector3.up * .5f);
                prop.Fuse.SetPosition(1, new Vector3(length, .9f, 0));
                prop.Ember.localPosition = new Vector3(length, .9f, 0);
                prop.Label.text = Mathf.CeilToInt(left / 60f) + "s";
            }
            foreach (var pair in _potatoes)
            {
                var prop = pair.Value;
                if (live.Contains(pair.Key)) continue;
                if (!prop.Retired) { prop.RetiredAt = view.Tick; prop.Retired = true; }
                long age = (long)view.Tick - prop.RetiredAt;
                bool fizzle = prop.RetiredAt < prop.Cue.FuseEnd && age >= 0 && age < 24 && view.Cursor.Gameplay;
                prop.Root.gameObject.SetActive(fizzle);
                for (int i = 0; i < prop.Root.childCount; i++) prop.Root.GetChild(i).gameObject.SetActive(false);
                prop.Ember.gameObject.SetActive(fizzle);
                prop.Ember.GetComponent<Renderer>().sharedMaterial = _dust;
                prop.Ember.localScale = Vector3.one * (.2f + age * .035f);
                prop.Ember.localPosition = Vector3.up * (age * .025f);
            }
            foreach (var prop in _potatoes.Values)
                if (!prop.Retired)
                {
                    for (int i = 0; i < prop.Root.childCount; i++) prop.Root.GetChild(i).gameObject.SetActive(true);
                    prop.Ember.GetComponent<Renderer>().sharedMaterial = _hot;
                    prop.Ember.localScale = Vector3.one * .18f;
                }
            foreach (var contestant in view.Contestants)
            {
                bool locked = view.Scene.Carries.Any(c => c.LastGiver == contestant.Seat);
                if (!_returnLocks.TryGetValue(contestant.Seat, out var mark) && locked)
                {
                    mark = PropRoot("Potato return lock");
                    Part(mark, "Lock bar", _hot, Vector3.zero, new Vector3(.8f, .1f, .1f));
                    var label = Lettering(mark, "Return lock", Vector3.up * .4f, .6f, Color.white); label.text = "LOCK";
                    _returnLocks.Add(contestant.Seat, mark);
                }
                if (mark == null) continue;
                mark.gameObject.SetActive(locked && view.Cursor.Gameplay);
                if (_fighters.TryGetValue(contestant.EntityId, out var fighter)) mark.position = fighter.transform.position + Vector3.up * 2;
            }
            foreach (var root in _barrages.Values) root.gameObject.SetActive(false);
            foreach (var cue in view.Scene.Bombs)
            {
                if (cue.Kind != MayhemBombKind.Barrage) continue;
                int key = cue.Lane * 65536 + cue.Ordinal;
                if (!_barrages.TryGetValue(key, out var root))
                {
                    root = PropRoot("Deck-wide barrage");
                    for (int i = 0; i < 8; i++)
                    {
                        float x = Mathf.Lerp(_deckMinX, _deckMaxX, (i + .5f) / 8);
                        Part(root, "Barrage warning stripe", _hot, new Vector3(x, _deckTop + .08f, (_deckMinZ + _deckMaxZ) * .5f),
                            new Vector3(.18f, .06f, _deckMaxZ - _deckMinZ));
                    }
                    var label = Lettering(root, "Barrage instruction", new Vector3(0, _deckTop + .22f, 0), 1.8f, Color.white);
                    label.transform.localRotation = Quaternion.Euler(65, 0, 0);
                    _barrages.Add(key, root);
                }
                long age = (long)view.Tick - cue.ImpactTick;
                root.gameObject.SetActive(view.Cursor.Gameplay && view.Tick >= cue.LaunchTick && age < 24);
                var text = root.GetComponentInChildren<TextMesh>(); text.text = age < 0 ? "HOLD SHIELD!" : "BLAST!";
                foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                    if (renderer.GetComponent<TextMesh>() == null) renderer.sharedMaterial = age < 0 ? _hot : _gold;
                for (int i = 0; i < 8; i++) root.GetChild(i).localScale = new Vector3(age < 0 ? .18f : 1.3f, age < 0 ? .06f : 1.6f, _deckMaxZ - _deckMinZ);
            }
        }
        private void ResetFinalChannels()
        {
            foreach (var root in _returnLocks.Values) Object.Destroy(root.gameObject); _returnLocks.Clear();
            foreach (var p in _potatoes.Values) Object.Destroy(p.Root.gameObject); _potatoes.Clear();
            foreach (var root in _barrages.Values) Object.Destroy(root.gameObject); _barrages.Clear();
            if (_hostStars != null) Object.Destroy(_hostStars.gameObject); _hostStars = null;
        }
    }
}

using System.Collections.Generic;
using SlopArena.Client.Animation;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private Mesh _starMesh, _propCone;
        private Transform _bombTemplateHolder;
        private Material _cardboard, _tape, _wood, _concrete, _dust, _tile, _stencilText, _selfiePink, _glitchGreen, _voidPanel;
        private readonly MaterialPropertyBlock _propTint = new();
        private readonly Dictionary<ulong, BoardStack> _boardStacks = new();
        private readonly Dictionary<int, StarSpot> _starSpots = new();
        private readonly Dictionary<int, BombTarget> _bombTargets = new();
        private readonly Dictionary<int, GroundWave> _groundWaves = new();
        private readonly List<int> _expiredWaves = new();
        private sealed class BoardStack
        {
            public Transform Root;
            public Transform[] Halves;
            public uint[] SplitTicks;
        }
        private sealed class StarSpot
        {
            public Transform Root;
            public Renderer Pad, Beam;
            public float InitialRadius;
            public LineRenderer Boundary;
            public TextMesh Label;
        }
        private sealed class BombTarget
        {
            public Transform Root, Shadow, Incoming;
            public LineRenderer Rim;
        }
        private sealed class GroundWave
        {
            public Transform Root;
            public Transform[] Tiles, Dust, Crest;
        }
        private void CreatePropMaterials()
        {
            _selfiePink = Material(new Color(1, .35f, .78f));
            _glitchGreen = Material(new Color(.12f, 1, .27f));
            _voidPanel = Material(new Color(.005f, .008f, .006f));
            _starMesh = StarMesh(); _propCone = BeamMesh();
            _deckMeshes.Add(_starMesh); _deckMeshes.Add(_propCone);
            _cardboard = Glow(new Color(.78f, .57f, .32f), .12f);
            _tape = Glow(new Color(.61f, .44f, .25f), .02f);
            _wood = Glow(new Color(.91f, .70f, .37f), .04f);
            _concrete = Glow(new Color(.37f, .39f, .37f), 0);
            _dust = Glow(new Color(.67f, .56f, .39f), .02f);
            _tile = Glow(new Color(.24f, .57f, .50f), .08f);
            _bombTemplateHolder = PropRoot("Bomb template storage"); _bombTemplateHolder.gameObject.SetActive(false);
            var bomb = new GameObject("Manki grin bomb").transform; bomb.SetParent(_bombTemplateHolder, false);
            DeckObject(bomb, "Matte shell", _sphereMesh, _tape, Vector3.zero, Vector3.one * .7f);
            for (int side = -1; side <= 1; side += 2)
            {
                DeckObject(bomb, "Painted eye", _sphereMesh, _wood, new Vector3(side * .13f, .1f, -.30f), new Vector3(.15f, .17f, .05f));
                DeckObject(bomb, "Pupil", _sphereMesh, _ink, new Vector3(side * .13f, .1f, -.33f), new Vector3(.055f, .075f, .025f));
                DeckObject(bomb, "Manki ear", _sphereMesh, _cardboard, new Vector3(side * .34f, .05f, 0), new Vector3(.16f, .2f, .12f));
            }
            DeckObject(bomb, "Grin ink", _sphereMesh, _ink, new Vector3(0, -.12f, -.29f), new Vector3(.43f, .24f, .12f));
            for (int tooth = 0; tooth < 5; tooth++)
                Part(bomb, "Painted tooth", _wood, new Vector3((tooth - 2) * .065f, -.12f - .025f * Mathf.Cos((tooth - 2) * .7f), -.355f), new Vector3(.054f, .09f, .018f));
            var fuse = Line(bomb, "Lit rope fuse", _wood, .045f); fuse.positionCount = 3;
            fuse.SetPosition(0, new Vector3(0, .30f, 0)); fuse.SetPosition(1, new Vector3(.02f, .48f, 0)); fuse.SetPosition(2, new Vector3(.12f, .54f, 0));
            DeckObject(bomb, "Steady fuse ember", _sphereMesh, _hot, new Vector3(.12f, .54f, 0), Vector3.one * .10f);
            _projectiles.RegisterHazardOwner(ServerSimulation.MayhemHostEntityId, Host.Catalog,
                new ProjectilePresentation(Host.Bomb.SlotId, bomb.gameObject, 1, ProjectileOrientation.PreservePrefab), Host.Hand);
        }
        private Transform Part(Transform root, string name, Material material, Vector3 p, Vector3 s)
            => DeckObject(root, name, _cubeMesh, material, p, s);
        private Transform PropRoot(string name)
        {
            var root = new GameObject(name).transform; root.SetParent(_root, false); return root;
        }
        private Transform CardboardWall(RuntimeSolid wall)
        {
            var root = PropRoot("Cardboard fort wall");
            float w = wall.HalfExtents.X * 2, h = wall.HalfExtents.Y * 2, d = wall.HalfExtents.Z * 2;
            bool weak = wall.HitBudget == MayhemPresentationTuning.CrackedWallHits;
            for (int i = 0; i < 6; i++)
            {
                float x = ((i % 3) - 1) * w / 3;
                float y = (i / 3 - .5f) * h / 2;
                var panel = Part(root, "Corrugated cardboard box", _cardboard, new Vector3(x, y, 0), new Vector3(w / 3 - .025f, h / 2 - .025f, d));
                for (int face = -1; face <= 1; face += 2)
                {
                    Part(panel, "Kraft packing tape", _tape, new Vector3(0, 0, face * .51f), new Vector3(.22f, 1, .025f));
                    Part(panel, "Folded box seam", _tape, new Vector3(0, 0, face * .515f), new Vector3(1, .018f, .025f));
                    var patch = Part(panel, "Torn kraft paper patch", _wood, new Vector3(.29f, -.27f, face * .53f), new Vector3(.24f, .09f, .018f));
                    patch.localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? 19 : -24);
                    for (int mark = 0; mark < 2; mark++)
                    {
                        float mx = -.31f + mark * .12f;
                        Part(panel, "Printed handling arrow", _tape, new Vector3(mx, .12f, face * .53f), new Vector3(.025f, .17f, .018f));
                        var stamp = Part(panel, "Printed arrow head", _tape, new Vector3(mx, .21f, face * .53f), new Vector3(.065f, .035f, .018f));
                        stamp.localRotation = Quaternion.Euler(0, 0, 45);
                    }
                }
                if (i >= 3)
                {
                    var flap = Part(panel, "Open cardboard flap", _wood, new Vector3(0, .50f, 0), new Vector3(.98f, .025f, 1.8f));
                    flap.localRotation = Quaternion.Euler(12, 0, 0);
                    for (int flute = 0; flute < 12; flute++)
                        Part(panel, "Corrugated cut edge", _tape, new Vector3((flute - 5.5f) / 12, .505f, -.51f), new Vector3(.025f, .025f, .025f));
                    if (weak)
                    {
                        Part(panel, "FRAGILE top rim", _hot, new Vector3(0, .54f, 0), new Vector3(1, .055f, 2.3f));
                        for (int stripe = 0; stripe < 4; stripe++)
                        {
                            var tape = Part(panel, "Hazard tape stripe", _ink, new Vector3((stripe - 1.5f) * .24f, .575f, 0), new Vector3(.10f, .025f, 2.3f));
                            tape.localRotation = Quaternion.Euler(0, 18, 0);
                        }
                        var tear = Part(panel, "Bold top tear", _gold, new Vector3(0, .61f, 0), new Vector3(.9f, .035f, .20f));
                        tear.localRotation = Quaternion.Euler(0, i % 2 == 0 ? 22 : -22, 0);
                    }
                }
            }
            if (weak)
            {
                var outward = new Vector3(wall.Center.X - wall.CageCenter.X, 0, wall.Center.Z - wall.CageCenter.Z).normalized;
                var local = Quaternion.Euler(0, -wall.Yaw * Mathf.Rad2Deg, 0) * outward;
                var decal = new GameObject("FRAGILE exit deck decal").transform; decal.SetParent(root, false);
                decal.localPosition = local * (d * .5f + .85f) + Vector3.down * (h * .5f - .10f);
                decal.localRotation = Quaternion.LookRotation(local, Vector3.up);
                var label = Lettering(decal, "FRAGILE deck stencil", new Vector3(0, .02f, -.2f), .55f, new Color(1, .48f, .14f));
                label.text = "FRAGILE"; label.transform.localRotation = Quaternion.Euler(90, 0, 0);
                if (_stencilText == null)
                {
                    _stencilText = Material(new Color(1, .48f, .14f));
                    _stencilText.SetTexture("_BaseMap", label.GetComponent<Renderer>().sharedMaterial.mainTexture);
                    _stencilText.SetFloat("_AlphaClip", 1);
                    _stencilText.SetFloat("_Cutoff", .25f);
                    _stencilText.SetFloat("_Cull", 0);
                    _stencilText.EnableKeyword("_ALPHATEST_ON");
                }
                label.GetComponent<Renderer>().sharedMaterial = _stencilText;
                var arrow = Line(decal, "Exit arrow", _hot, .16f); arrow.positionCount = 5;
                arrow.SetPositions(new[] { new Vector3(0, .04f, 0), new Vector3(0, .04f, .65f), new Vector3(-.3f, .04f, .35f), new Vector3(0, .04f, .65f), new Vector3(.3f, .04f, .35f) });
            }
            return root;
        }
        private void CrumpleWall(Transform root, RuntimeSolid wall)
        {
            float damage = wall.HitBudget == 0 ? 0 : wall.HitBudget - wall.HitsLeft;
            for (int i = 0; i < 6; i++)
            {
                var panel = root.GetChild(i);
                float w = wall.HalfExtents.X * 2, h = wall.HalfExtents.Y * 2;
                float sign = i % 2 == 0 ? 1 : -1;
                panel.localPosition = new Vector3(((i % 3) - 1) * w / 3, (i / 3 - .5f) * h / 2 - damage * (i >= 3 ? .48f : .10f), damage * sign * .22f);
                panel.localRotation = Quaternion.Euler(damage * sign * 16, damage * sign * 11, damage * sign * 12);
                panel.localScale = new Vector3(w / 3 - .025f, (h / 2 - .025f) * (1 - damage * .16f), wall.HalfExtents.Z * 2);
            }
        }
        private void UpdateGroundWaves(MayhemShowView view)
        {
            _expiredWaves.Clear();
            foreach (var pair in _groundWaves) _expiredWaves.Add(pair.Key);
            for (int i = 0; i < view.Scene.Rings.Count; i++)
            {
                var cue = view.Scene.Rings[i];
                var marker = Prop("Slam origin " + cue.Lane + "/" + cue.Ordinal, PrimitiveType.Cylinder, _hot);
                marker.position = new Vector3(cue.Center.X, _deckTop + .06f, cue.Center.Z);
                marker.localScale = new Vector3(1.8f, .04f, 1.8f);
                marker.gameObject.SetActive(view.Cursor.Gameplay && (long)view.Tick >= (long)cue.SlamTick - 30 && view.Tick < cue.SlamTick + 12);
            }
            for (int ringIndex = 0; ringIndex < view.Scene.Rings.Count; ringIndex++)
            {
                var cue = view.Scene.Rings[ringIndex];
                long age = (long)view.Tick - cue.SlamTick;
                if (age < 0 || age >= cue.LifetimeTicks || !view.Cursor.Gameplay) continue;
                int key = cue.Lane * 256 + cue.Ordinal;
                _expiredWaves.Remove(key);
                if (!_groundWaves.TryGetValue(key, out var wave))
                {
                    wave = new GroundWave { Root = PropRoot("Flipping deck ground wave"), Tiles = new Transform[96], Dust = new Transform[48], Crest = new Transform[192] };
                    for (int i = 0; i < wave.Tiles.Length; i++) wave.Tiles[i] = Part(wave.Root, "Lifted tile", i % 3 == 0 ? _wood : _tile, Vector3.zero, Vector3.one);
                    for (int i = 0; i < wave.Dust.Length; i++) wave.Dust[i] = DeckObject(wave.Root, "Rolling dust", _sphereMesh, _dust, Vector3.zero, Vector3.one);
                    for (int i = 0; i < wave.Crest.Length; i++) wave.Crest[i] = Part(wave.Root, "Continuous danger crest", _hot, Vector3.zero, Vector3.one);
                    _groundWaves.Add(key, wave);
                }
                float radius = age * cue.Speed / 60f;
                wave.Root.position = Point(cue.Center);
                bool paired = false;
                for (int otherIndex = 0; otherIndex < view.Scene.Rings.Count; otherIndex++)
                {
                    var other = view.Scene.Rings[otherIndex];
                    if (other.SlamTick != cue.SlamTick && System.Math.Abs((long)other.SlamTick - cue.SlamTick) < 60) { paired = true; break; }
                }
                float crest = cue.HalfHeight * 2;
                for (int i = 0; i < wave.Tiles.Length; i++)
                {
                    float a = i * Mathf.PI * 2 / wave.Tiles.Length;
                    float flip = Mathf.Sin(age * .25f + i * .8f);
                    var tile = wave.Tiles[i];
                    tile.localPosition = new Vector3(Mathf.Cos(a) * radius, -cue.HalfHeight + crest * .5f, Mathf.Sin(a) * radius);
                    tile.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 14 + flip * 12);
                    tile.localScale = new Vector3(cue.HalfWidth * 1.5f, crest, Mathf.Max(.02f, radius * Mathf.PI * 2 / wave.Tiles.Length * .92f));
                    tile.gameObject.SetActive(OnDeck(tile.position, tile.localScale.magnitude * .5f));
                }
                for (int i = 0; i < wave.Dust.Length; i++)
                {
                    var dust = wave.Dust[i]; dust.gameObject.SetActive(paired || i % 2 == 0);
                    float a = (i + .3f) * Mathf.PI * 2 / wave.Dust.Length;
                    float size = cue.HalfWidth * (paired ? 1 : .65f) * (1 + .2f * Mathf.Sin(age * .17f + i));
                    dust.localPosition = new Vector3(Mathf.Cos(a) * radius, -cue.HalfHeight + crest * .6f + .04f, Mathf.Sin(a) * radius);
                    dust.localScale = new Vector3(size, size * .7f, size);
                    dust.gameObject.SetActive((paired || i % 2 == 0) && OnDeck(dust.position, size * .5f));
                }
                for (int i = 0; i < wave.Crest.Length; i++)
                {
                    float a = i * Mathf.PI * 2 / wave.Crest.Length;
                    var rim = wave.Crest[i];
                    rim.localPosition = new Vector3(Mathf.Cos(a) * radius, -cue.HalfHeight + crest + .05f, Mathf.Sin(a) * radius);
                    rim.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                    rim.localScale = new Vector3(.16f, .12f, Mathf.Max(.02f, radius * Mathf.PI * 2 / wave.Crest.Length * 1.04f));
                    rim.gameObject.SetActive(OnDeck(rim.position, rim.localScale.magnitude * .5f));
                }
            }
            foreach (int key in _expiredWaves) { Object.Destroy(_groundWaves[key].Root.gameObject); _groundWaves.Remove(key); }
        }
        private bool OnDeck(Vector3 point, float margin)
            => point.x >= _deckMinX + margin && point.x <= _deckMaxX - margin && point.z >= _deckMinZ + margin && point.z <= _deckMaxZ - margin;
        private BoardStack CreateBoards(TargetCue target)
        {
            var stack = new BoardStack { Root = PropRoot("Karate board stack"), Halves = new Transform[target.Hits * 2], SplitTicks = new uint[target.Hits] };
            for (int side = -1; side <= 1; side += 2)
            {
                var block = Part(stack.Root, "Cinder block", _concrete, new Vector3(side * target.Radius * .67f, -target.HalfHeight * .7f, 0), new Vector3(target.Radius * .45f, target.HalfHeight * .55f, target.Radius * .9f));
                for (int hole = -1; hole <= 1; hole += 2)
                    Part(block, "Block hollow", _ink, new Vector3(0, .505f, hole * .23f), new Vector3(.62f, .02f, .29f));
            }
            for (int i = 0; i < target.Hits; i++) for (int side = 0; side < 2; side++)
            {
                var half = Part(stack.Root, "Split board half", _wood, Vector3.zero, new Vector3(target.Radius, .12f, target.Radius * .75f));
                for (int grain = 0; grain < 3; grain++)
                    Part(half, "Wood grain", _cardboard, new Vector3(0, .505f, (grain - 1) * .24f), new Vector3(.84f, .015f, .022f));
                stack.Halves[i * 2 + side] = half;
            }
            return stack;
        }
        private sealed class VarietyZone
        {
            public Transform Root, Fill, Camera;
            public LineRenderer Edge;
            public TextMesh Label;
        }
        private sealed class BonkHead
        {
            public Transform Root;
            public ushort Ordinal;
            public MayhemTargetKind Kind;
        }
        private readonly Dictionary<int, VarietyZone> _varietyZones = new();
        private readonly Dictionary<ulong, BonkHead> _bonkHeads = new();
        private Transform _glitchGrid;
        private void UpdateVarietyZone(ZoneCue cue, MayhemShowView view)
        {
            int key = cue.Lane * 256 + cue.Ordinal;
            if (!_varietyZones.TryGetValue(key, out var prop))
            {
                var root = PropRoot(cue.Kind.ToString());
                prop = new VarietyZone { Root = root, Edge = Line(root, "Cue boundary", _gold, .16f) };
                prop.Fill = Part(root, "Panel face", _tile, Vector3.zero, Vector3.one);
                prop.Label = Lettering(root, "Cue instruction", new Vector3(0, .18f, 0), .7f, Color.white);
                prop.Label.transform.localRotation = Quaternion.Euler(70, 0, 0);
                if (cue.Kind == MayhemZoneKind.SelfieFrame)
                {
                    prop.Camera = new GameObject("Nyx selfie camera").transform;
                    prop.Camera.SetParent(root, false);
                    Part(prop.Camera, "Pink camera body", _selfiePink, Vector3.zero, new Vector3(.9f, .6f, .3f));
                    DeckObject(prop.Camera, "Camera lens", _sphereMesh, _ink, new Vector3(0, 0, -.2f), Vector3.one * .35f);
                    Part(prop.Camera, "Flash bulb", _gold, new Vector3(.3f, .3f, -.15f), Vector3.one * .2f);
                }
                _varietyZones.Add(key, prop);
            }
            bool selfie = cue.Kind == MayhemZoneKind.SelfieFrame;
            bool ring = cue.Kind == MayhemZoneKind.SumoRing;
            long flashAge = (long)view.Tick - cue.ArmedFrom;
            bool flash = selfie && flashAge >= 0 && flashAge < 8;
            prop.Root.gameObject.SetActive(view.Cursor.Gameplay && (cue.ActiveAt(view.Tick) || flash));
            var center = cue.CenterAt(view.Tick);
            prop.Root.position = new Vector3(center.X, _deckTop + .12f, center.Z);
            float width = cue.RadiusAt(view.Tick), depth = cue.HalfDepthAt(view.Tick);
            bool armed = cue.ArmedAt(view.Tick);
            prop.Edge.sharedMaterial = ring ? (cue.ActiveUntil <= view.Tick + 90 ? _hot : _wood)
                : selfie ? (flash ? _gold : _selfiePink) : armed ? _hot : _glitchGreen;
            prop.Edge.startWidth = prop.Edge.endWidth = ring ? .22f : selfie ? .24f : .12f;
            if (ring) SetCircle(prop.Edge, 1, width);
            else
            {
                prop.Edge.positionCount = 5;
                prop.Edge.SetPositions(new[] { new Vector3(-width, 0, -depth), new Vector3(-width, 0, depth),
                    new Vector3(width, 0, depth), new Vector3(width, 0, -depth), new Vector3(-width, 0, -depth) });
            }
            if (prop.Camera != null) prop.Camera.localPosition = new Vector3(width, .5f, -depth);
            prop.Fill.gameObject.SetActive(!ring && (!selfie || flash));
            prop.Fill.GetComponent<Renderer>().sharedMaterial = selfie ? _gold : armed ? _voidPanel : _glitchGreen;
            prop.Fill.localScale = new Vector3(width * 2 - .08f, .025f, depth * 2 - .08f);
            var tint = selfie ? Color.white : armed ? new Color(.005f, .008f, .006f) : new Color(.12f, 1, .27f);
            if (!selfie && !armed && !ClientSettingsService.Instance.ReducedFlashing)
                tint *= .65f + .35f * Mathf.Sin(view.Tick * .35f + cue.Ordinal);
            _propTint.SetColor("_BaseColor", tint);
            _propTint.SetColor("_EmissionColor", Color.black);
            prop.Fill.GetComponent<Renderer>().SetPropertyBlock(_propTint);
            prop.Label.transform.localPosition = new Vector3(0, .18f, ring ? -width - .5f : -depth - .45f);
            prop.Label.text = ring ? "STAY IN!" : selfie ? (flashAge >= 0 ? "SNAP!" : "SMILE! " + Mathf.CeilToInt((cue.ArmedFrom - view.Tick) / 60f)) : armed ? "GONE!" : "JUMP OR MOVE!";
            prop.Label.color = ring && cue.ActiveUntil <= view.Tick + 90 ? new Color(1, .3f, .1f)
                : selfie ? new Color(1, .55f, .87f) : Color.green;
        }
        private void UpdateBonkHead(TargetCue cue, MayhemShowView view)
        {
            if (_bonkHeads.TryGetValue(cue.EntityId, out var head) && (head.Ordinal != cue.Ordinal || head.Kind != cue.Kind))
            {
                Object.Destroy(head.Root.gameObject); _bonkHeads.Remove(cue.EntityId); head = null;
            }
            if (head == null)
            {
                head = new BonkHead { Root = PropRoot("Cardboard Manki head"), Ordinal = cue.Ordinal, Kind = cue.Kind };
                Part(head.Root, "Cardboard face", _cardboard, Vector3.zero, new Vector3(1.25f, 1.35f, .18f));
                for (int side = -1; side <= 1; side += 2)
                {
                    DeckObject(head.Root, "Round cardboard ear", _sphereMesh, _cardboard, new Vector3(side * .72f, .12f, 0), new Vector3(.4f, .5f, .18f));
                    Part(head.Root, "Painted eye", _ink, new Vector3(side * .25f, .25f, -.11f), new Vector3(.13f, .2f, .03f));
                }
                Part(head.Root, "Grin", _ink, new Vector3(0, -.22f, -.11f), new Vector3(.8f, .25f, .03f));
                Part(head.Root, "Teeth", _wood, new Vector3(0, -.18f, -.14f), new Vector3(.65f, .08f, .02f));
                Part(head.Root, "Wooden pop-up stick", _wood, new Vector3(0, -.8f, .06f), new Vector3(.16f, .5f, .15f));
                if (cue.Kind == MayhemTargetKind.DecoyHead)
                {
                    var fuse = Line(head.Root, "Lit decoy fuse", _hot, .1f); fuse.positionCount = 3;
                    fuse.SetPositions(new[] { new Vector3(0, .65f, 0), new Vector3(.1f, 1, 0), new Vector3(.4f, 1.15f, 0) });
                    DeckObject(head.Root, "Burning fuse", _sphereMesh, _hot, new Vector3(.4f, 1.15f, 0), Vector3.one * .25f);
                    var label = Lettering(head.Root, "Decoy warning", new Vector3(0, .75f, -.2f), .65f, new Color(1, .3f, .1f)); label.text = "BOMB!";
                }
                _bonkHeads.Add(cue.EntityId, head);
            }
            bool claimed = cue.HitsLeft == 0;
            head.Root.gameObject.SetActive(view.Cursor.Gameplay && view.Tick >= cue.ActiveFrom && view.Tick < cue.ActiveUntil);
            float rise = Mathf.Clamp01(((long)view.Tick - cue.ActiveFrom) / 8f);
            float duck = Mathf.Clamp01(((long)cue.ActiveUntil - view.Tick) / 8f);
            head.Root.position = new Vector3(cue.Center.X, claimed ? _deckTop + .12f : _deckTop + .8f * Mathf.Min(rise, duck), cue.Center.Z);
            head.Root.localScale = claimed ? new Vector3(1.3f, .12f, 1) : new Vector3(1, Mathf.Max(.08f, Mathf.Min(rise, duck)), 1);
            head.Root.localRotation = claimed ? Quaternion.Euler(75, 0, 0) : Quaternion.identity;
        }
        private void UpdateVarietyProps(MayhemShowView view)
        {
            foreach (var zone in _varietyZones.Values) zone.Root.gameObject.SetActive(false);
            foreach (var head in _bonkHeads.Values) head.Root.gameObject.SetActive(false);
            bool glitch = view.Channel?.Id == MayhemChannels.Glitch;
            if (glitch && _glitchGrid == null)
            {
                _glitchGrid = PropRoot("Glitch deck grid");
                for (int i = 0; i <= 4; i++)
                {
                    float x = Mathf.Lerp(_deckMinX, _deckMaxX, i / 4f), z = Mathf.Lerp(_deckMinZ, _deckMaxZ, i / 4f);
                    var column = Line(_glitchGrid, "Grid column", _tile, .08f); column.positionCount = 2;
                    column.SetPositions(new[] { new Vector3(x, _deckTop + .08f, _deckMinZ), new Vector3(x, _deckTop + .08f, _deckMaxZ) });
                    var row = Line(_glitchGrid, "Grid row", _tile, .08f); row.positionCount = 2;
                    row.SetPositions(new[] { new Vector3(_deckMinX, _deckTop + .08f, z), new Vector3(_deckMaxX, _deckTop + .08f, z) });
                }
            }
            if (_glitchGrid != null) _glitchGrid.gameObject.SetActive(glitch && view.Cursor.Gameplay);
            foreach (var cue in view.Scene.Zones)
                if (cue.Kind != MayhemZoneKind.Spotlight) UpdateVarietyZone(cue, view);
            foreach (var cue in view.Scene.Targets)
                if (cue.Kind == MayhemTargetKind.CardboardHead || cue.Kind == MayhemTargetKind.DecoyHead) UpdateBonkHead(cue, view);
        }
        private void UpdateCraftProps(MayhemShowView view)
        {
            UpdateVarietyProps(view);
            UpdateFinalChannels(view);
            for (int targetIndex = 0; targetIndex < view.Scene.Targets.Count; targetIndex++)
            {
                var target = view.Scene.Targets[targetIndex];
                if (target.Kind != MayhemTargetKind.BoardStack) continue;
                if (!_boardStacks.TryGetValue(target.EntityId, out var stack)) { stack = CreateBoards(target); _boardStacks.Add(target.EntityId, stack); }
                stack.Root.position = Point(target.Center);
                var colour = Color.white;
                if (target.OwnerSeat is byte seat && seat < view.Contestants.Count)
                    colour = SeatColours.For(_view.Find(view.Contestants[seat].EntityId).PlayerNumber);
                _propTint.SetColor("_BaseColor", colour);
                foreach (var half in stack.Halves) half.GetComponent<Renderer>().SetPropertyBlock(_propTint);
                for (int i = 0; i < target.Hits; i++)
                {
                    bool broken = i >= target.HitsLeft;
                    if (broken && stack.SplitTicks[i] == 0) stack.SplitTicks[i] = view.Tick;
                    if (!broken) stack.SplitTicks[i] = 0;
                    float age = broken ? Mathf.Min(1, (view.Tick - stack.SplitTicks[i]) / 60f) : 0;
                    for (int side = 0; side < 2; side++)
                    {
                        var half = stack.Halves[i * 2 + side]; float sign = side == 0 ? -1 : 1;
                        float y = -target.HalfHeight * .35f + (i + .5f) * .17f;
                        half.localPosition = new Vector3(sign * (target.Radius * .5f + age * .65f), Mathf.Max(_deckTop + .09f - target.Center.Y, y + age * .7f - age * age * 5), 0);
                        half.localRotation = Quaternion.Euler(0, 0, broken ? sign * Mathf.Min(75, age * 230) : 0);
                    }
                }
            }
            for (int zoneIndex = 0; zoneIndex < view.Scene.Zones.Count; zoneIndex++)
            {
                var zone = view.Scene.Zones[zoneIndex];
                if (zone.Kind != MayhemZoneKind.Spotlight) continue;
                int key = zone.Lane * 256 + zone.Ordinal;
                if (!_starSpots.TryGetValue(key, out var spot))
                {
                    var root = PropRoot("Star spotlight");
                    var boundary = Circle(root, "Exact scoring boundary", _wood, .10f, 1, zone.Radius);
                    var mesh = _starMesh;
                    var pad = DeckObject(root, "Silver scoring star", mesh, _wood, new Vector3(0, .025f, 0), Vector3.one * zone.Radius * .94f);
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i * Mathf.PI / 5;
                        Part(root, "Worn paint scratch", _concrete, new Vector3(Mathf.Cos(a) * zone.Radius * .7f, .03f, Mathf.Sin(a) * zone.Radius * .7f), new Vector3(.16f, .008f, .04f));
                    }
                    var cone = DeckObject(root, "Theatrical cone", _propCone, _beamMaterial, Vector3.up * 8, new Vector3(zone.Radius, zone.Radius, 8));
                    cone.localRotation = Quaternion.Euler(90, 0, 0);
                    DeckObject(root, "Overhead lamp", _sphereMesh, _tape, Vector3.up * 8, new Vector3(.7f, .45f, .7f));
                    var label = Lettering(root, "Spotlight scoring window", new Vector3(0, .05f, -zone.Radius - .4f), .45f, Color.white);
                    label.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    spot = new StarSpot { Root = root, Pad = pad.GetComponent<Renderer>(), Beam = cone.GetComponent<Renderer>(), InitialRadius = zone.Radius, Boundary = boundary, Label = label }; _starSpots.Add(key, spot);
                }
                spot.Root.gameObject.SetActive(zone.ActiveAt(view.Tick) && view.Cursor.Gameplay);
                var center = zone.CenterAt(view.Tick);
                float radius = zone.RadiusAt(view.Tick);
                spot.Root.position = Point(center) + Vector3.up * .08f;
                spot.Root.localScale = new Vector3(radius / spot.InitialRadius, 1, radius / spot.InitialRadius);
                spot.Boundary.startWidth = spot.Boundary.endWidth = zone.ArmedAt(view.Tick) ? .24f : .07f;
                spot.Boundary.sharedMaterial = zone.ArmedAt(view.Tick) ? _gold : _wood;
                spot.Label.text = zone.ArmedAt(view.Tick) ? "COUNTS AT BUZZER" : "KEEP MOVING";
                Color colour = new Color(.74f, .79f, .84f); float nearest = float.MaxValue; int seat = int.MaxValue;
                foreach (var contestant in view.Contestants)
                {
                    if (!contestant.ObjectiveMet || contestant.Withdrawn) continue;
                    var state = _sim.GetState(contestant.EntityId);
                    float dx = state.PX - center.X, dz = state.PZ - center.Z, distance = dx * dx + dz * dz;
                    if (distance > radius * radius || distance > nearest || distance == nearest && contestant.Seat >= seat) continue;
                    nearest = distance; seat = contestant.Seat; colour = SeatColours.For(_view.Find(contestant.EntityId).PlayerNumber);
                }
                _propTint.SetColor("_BaseColor", colour); spot.Pad.SetPropertyBlock(_propTint);
                colour.a = seat == int.MaxValue ? .045f : .24f; _propTint.SetColor("_BaseColor", colour); spot.Beam.SetPropertyBlock(_propTint);
            }
        }
        private static Mesh StarMesh()
        {
            var vertices = new Vector3[11]; var triangles = new int[30];
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI / 5; float r = i % 2 == 0 ? 1 : .43f;
                vertices[i + 1] = new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % 10 + 1;
            }
            var mesh = new Mesh { name = "Painted star", vertices = vertices, triangles = triangles }; mesh.RecalculateNormals(); return mesh;
        }
        private void UpdateBombTargets(MayhemShowView view)
        {
            for (int bombIndex = 0; bombIndex < view.Scene.Bombs.Count; bombIndex++)
            {
                var bomb = view.Scene.Bombs[bombIndex];
                if (bomb.Kind == MayhemBombKind.Barrage) continue;
                bool cage = view.Airing is SoloAiring solo && solo.Channel == MayhemChannels.BreakOut
                    || view.Airing is RemixAiring remix && remix.Goal.Channel == MayhemChannels.BreakOut && bomb.Lane == 0;
                if (!bomb.Launched && !cage) continue;
                int key = bomb.Lane * 256 + bomb.Ordinal;
                if (!_bombTargets.TryGetValue(key, out var target))
                {
                    var root = PropRoot("Bomb crosshair");
                    Circle(root, "Blast boundary", _gold, .12f, 1, bomb.BlastRadius);
                    var rim = Circle(root, "Pulsing crosshair rim", _hot, .09f, 1, bomb.BlastRadius);
                    rim.transform.localPosition = Vector3.up * .012f;
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * .5f;
                        var mark = Part(root, "Crosshair", _gold, new Vector3(Mathf.Cos(a), .015f, Mathf.Sin(a)) * bomb.BlastRadius * .85f, new Vector3(bomb.BlastRadius * .3f, .035f, .12f));
                        mark.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                    }
                    var shadow = DeckObject(root, "Growing impact shadow", _sphereMesh, _ink, Vector3.zero, Vector3.one);
                    var incoming = new GameObject("Incoming bomb at frame edge").transform; incoming.SetParent(root, false);
                    DeckObject(incoming, "Incoming shell", _sphereMesh, _ink, Vector3.zero, Vector3.one * .65f);
                    var streak = Line(incoming, "Bright fall streak", _hot, .12f); streak.positionCount = 2;
                    streak.SetPositions(new[] { Vector3.up * .4f, Vector3.up * 1.2f });
                    var chevron = Line(incoming, "Downward arrow", _gold, .12f); chevron.positionCount = 3;
                    chevron.SetPositions(new[] { new Vector3(-.35f, -.4f, 0), new Vector3(0, -.75f, 0), new Vector3(.35f, -.4f, 0) });
                    target = new BombTarget { Root = root, Rim = rim, Shadow = shadow, Incoming = incoming }; _bombTargets.Add(key, target);
                }
                target.Root.position = Point(bomb.Landing) + Vector3.up * .09f;
                target.Root.gameObject.SetActive(!bomb.Exploded && view.Tick <= bomb.ImpactTick && view.Cursor.Gameplay);
                float progress = Mathf.Clamp01(((long)view.Tick - bomb.LaunchTick) / (float)System.Math.Max(1u, bomb.ImpactTick - bomb.LaunchTick));
                float pulse = .85f + .15f * Mathf.Sin(progress * Mathf.PI * (ClientSettingsService.Instance.ReducedFlashing ? 2 : 8));
                var camera = _camera();
                var landing = camera.WorldToViewportPoint(Point(bomb.Landing));
                var marker = new Vector3(Mathf.Clamp(landing.x, .06f, .94f), .72f, landing.z);
                target.Incoming.gameObject.SetActive(bomb.Launched);
                target.Shadow.gameObject.SetActive(bomb.Launched);
                target.Incoming.position = camera.ViewportToWorldPoint(marker);
                target.Incoming.rotation = camera.transform.rotation;
                target.Incoming.localScale = Vector3.one * Mathf.Max(1f, bomb.BlastRadius / DodgeTuning.BlastRadius);
                target.Rim.startWidth = target.Rim.endWidth = .19f * pulse;
                target.Shadow.localScale = new Vector3(1, .006f, 1) * Mathf.Lerp(.2f, bomb.BlastRadius * 1.3f, progress);
            }
        }
        private void ResetCraftProps()
        {
            ResetFinalChannels();
            foreach (var p in _varietyZones.Values) Object.Destroy(p.Root.gameObject); _varietyZones.Clear();
            foreach (var p in _bonkHeads.Values) Object.Destroy(p.Root.gameObject); _bonkHeads.Clear();
            if (_glitchGrid != null) Object.Destroy(_glitchGrid.gameObject); _glitchGrid = null;
            foreach (var p in _boardStacks.Values) Object.Destroy(p.Root.gameObject); _boardStacks.Clear();
            foreach (var p in _starSpots.Values) Object.Destroy(p.Root.gameObject); _starSpots.Clear();
            foreach (var p in _bombTargets.Values) Object.Destroy(p.Root.gameObject); _bombTargets.Clear();
            foreach (var p in _groundWaves.Values) Object.Destroy(p.Root.gameObject); _groundWaves.Clear();
        }
        private void DisposeCraftProps()
        {
            ResetCraftProps();
            Object.Destroy(_bombTemplateHolder.gameObject);
            foreach (var material in new[] { _cardboard, _tape, _wood, _concrete, _dust, _tile, _stencilText, _selfiePink, _glitchGreen, _voidPanel }) Object.Destroy(material);
        }
    }
}

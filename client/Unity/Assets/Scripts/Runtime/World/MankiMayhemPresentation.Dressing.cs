using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlopArena.Client.World
{
    public sealed partial class MankiMayhemPresentation
    {
        private readonly Transform[] _floorDecals = new Transform[3];
        private Material _decalInk, _onAirGlow;
        private readonly Material[] _pennantInk = new Material[3];
        private TextMesh _onAirText;

        private void CreateDeckDressing()
        {
            _decalInk = Glow(new Color(.45f, .5f, .45f), .05f);
            _onAirGlow = Glow(new Color(.16f, .045f, .02f), .1f);
            var booth = new GameObject("Booth ON AIR").transform;
            booth.SetParent(_deckRoot, false);
            booth.position = new Vector3(0, _deckTop + 4.2f, 30);
            DeckObject(booth, "Sign frame", _cubeMesh, _rigInk, Vector3.zero, new Vector3(8, 1.6f, .3f));
            DeckObject(booth, "Sign glow", _cubeMesh, _onAirGlow, new Vector3(0, 0, -.2f), new Vector3(7.7f, 1.35f, .1f));
            _onAirText = Lettering(booth, "ON AIR lettering", new Vector3(0, 0, -.3f), 1.3f, Color.white);
            _onAirText.text = "ON AIR";
            var pennant = new Mesh { name = "Stand pennant",
                vertices = new[] { new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(0, -.5f, 0),
                    new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(0, -.5f, 0) },
                triangles = new[] { 0, 1, 2, 5, 4, 3 } };
            pennant.RecalculateNormals(); _deckMeshes.Add(pennant);
            for (int i = 0; i < 3; i++) _pennantInk[i] = Glow(
                i == 0 ? new Color(.55f, .18f, .08f) : i == 1 ? new Color(.35f, .16f, .45f) : new Color(.65f, .42f, .08f), .12f);
            var banners = new[] { new List<CombineInstance>(), new List<CombineInstance>(), new List<CombineInstance>() };
            for (int side = -1; side <= 1; side += 2)
            {
                var lightRoot = new GameObject("Warm audience fill").transform;
                lightRoot.SetParent(_deckRoot, false);
                lightRoot.position = new Vector3(side * (_deckMaxX + 4), _deckTop + 6, 12);
                var light = lightRoot.gameObject.AddComponent<Light>();
                light.type = LightType.Point; light.color = new Color(1, .62f, .32f);
                light.intensity = 3; light.range = 22; light.shadows = LightShadows.None;
                for (int i = 0; i < 9; i++)
                {
                    float z = _deckMinZ + 3 + i * 3;
                    float y = _deckTop + 2.8f - Mathf.Sin(i * Mathf.PI / 8) * .7f;
                    banners[i % 3].Add(new CombineInstance { mesh = pennant,
                        transform = Matrix4x4.TRS(new Vector3(side * (_deckMaxX + 2.2f), y, z),
                            Quaternion.Euler(0, side * 50, 0), new Vector3(.95f, 1.1f, 1)) });
                }
            }
            for (int i = 0; i < 3; i++) CombineDeck(_deckRoot, "Stand pennants " + i, banners[i], _pennantInk[i]);
            for (int kind = 0; kind < _floorDecals.Length; kind++)
            {
                var parent = new GameObject("Floor print " + (MayhemFloorDecal)kind).transform;
                parent.SetParent(_deckRoot, false); _floorDecals[kind] = parent;
                var parts = new List<CombineInstance>();
                void Stroke(Vector2 a, Vector2 b, float width)
                {
                    var d = b - a;
                    parts.Add(new CombineInstance { mesh = _cubeMesh, transform = Matrix4x4.TRS(
                        new Vector3((a.x + b.x) * .5f, _deckTop + .048f, (a.y + b.y) * .5f - 3),
                        Quaternion.Euler(0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 0), new Vector3(d.magnitude + width * .3f, .006f, width)) });
                }
                void Ring(float radius, float width)
                {
                    for (int j = 0; j < 64; j++)
                    {
                        float a = j * Mathf.PI / 32, b = (j + 1) * Mathf.PI / 32;
                        Stroke(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, width);
                    }
                }
                if ((MayhemFloorDecal)kind == MayhemFloorDecal.Target)
                {
                    Ring(4, .3f); Ring(2.3f, .25f); Ring(.6f, .4f);
                    Stroke(new Vector2(-4.7f, 0), new Vector2(4.7f, 0), .18f);
                    Stroke(new Vector2(0, -4.7f), new Vector2(0, 4.7f), .18f);
                }
                else if ((MayhemFloorDecal)kind == MayhemFloorDecal.PoolRules)
                {
                    Ring(4, .4f);
                    Stroke(new Vector2(-2.8f, -2.8f), new Vector2(2.8f, 2.8f), .5f);
                    Stroke(new Vector2(.5f, 1.5f), new Vector2(.5f, 2.1f), .9f);
                    Stroke(new Vector2(.2f, 1.1f), new Vector2(-.6f, -.5f), .55f);
                    Stroke(new Vector2(-.6f, -.5f), new Vector2(1.3f, -1.2f), .5f);
                    Stroke(new Vector2(1.3f, -1.2f), new Vector2(1.7f, -2.2f), .45f);
                    Stroke(new Vector2(-.6f, -.5f), new Vector2(-2, -1.5f), .45f);
                    Stroke(new Vector2(-.1f, .6f), new Vector2(-1.6f, 1), .4f);
                    Stroke(new Vector2(-.1f, .6f), new Vector2(1.7f, .1f), .4f);
                }
                else
                {
                    for (int j = 0; j < 10; j++)
                    {
                        float a = j * Mathf.PI / 5, b = (j + 1) * Mathf.PI / 5;
                        Stroke(new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * (j % 2 == 0 ? 4 : 1.8f),
                            new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * (j % 2 == 0 ? 1.8f : 4), .45f);
                    }
                }
                CombineDeck(parent, "Printed emblem", parts, _decalInk);
                parent.gameObject.SetActive(false);
            }
        }
        private void UpdateDeckDressing(ChannelLook look, bool playing)
        {
            for (int i = 0; i < _floorDecals.Length; i++) _floorDecals[i].gameObject.SetActive(i == (int)look.Decal);
            Tint(_decalInk, Color.Lerp(look.A, look.Accent, .32f), .03f);
            Tint(_onAirGlow, playing ? new Color(1, .22f, .025f) : new Color(.12f, .04f, .025f), playing ? 1 : .05f);
            _onAirText.color = playing ? new Color(1, .95f, .7f) : new Color(.45f, .32f, .22f);
        }
        private void DisposeDeckDressing()
        {
            foreach (var material in _pennantInk) Object.Destroy(material);
            Object.Destroy(_decalInk); Object.Destroy(_onAirGlow);
        }
    }
}

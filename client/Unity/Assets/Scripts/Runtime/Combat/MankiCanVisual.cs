using System.Collections.Generic;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client.Combat
{
    public sealed class MankiCanVisual : MonoBehaviour
    {
        private readonly List<Material> _materials = new();
        private Material _body;
        private Transform _spark;

        public static MankiCanVisual Create()
        {
            var root = new GameObject("Manki Can");
            var visual = root.AddComponent<MankiCanVisual>();
            visual._body = visual.Material(new Color(1f, .78f, .04f));
            var red = visual.Material(new Color(.8f, .025f, .015f));
            var metal = visual.Material(new Color(.16f, .13f, .1f));
            var glow = visual.Material(new Color(1f, .32f, .025f));
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(4f, 1.2f, .05f));
            visual.Part("Yellow body", PrimitiveType.Cylinder, new(0f, .3f, 0f), new(.34f, .28f, .34f), visual._body);
            visual.Part("Red band", PrimitiveType.Cylinder, new(0f, .35f, 0f), new(.345f, .07f, .345f), red);
            visual.Part("Cap", PrimitiveType.Cylinder, new(0f, .6f, 0f), new(.3f, .025f, .3f), metal);
            visual.Part("Fuse", PrimitiveType.Cylinder, new(.04f, .7f, 0f), new(.025f, .075f, .025f), metal);
            visual._spark = visual.Part("Fuse spark", PrimitiveType.Sphere, new(.04f, .78f, 0f), Vector3.one * .09f, glow);
            var light = visual._spark.gameObject.AddComponent<Light>();
            light.color = new Color(1f, .25f, .025f); light.intensity = .7f; light.range = 1.2f;
            return visual;
        }

        public void Apply(in MankiCanState can, uint tick)
        {
            transform.position = new(can.X, can.Y, can.Z);
            float warning = can.IsTriggered ? 1f - can.WarningTicksLeft / (float)can.Payload.WarningTicks : 0f;
            transform.localScale = Vector3.one * (1f + .45f * warning);
            _body.color = Color.Lerp(new Color(1f, .78f, .04f), new Color(1f, .015f, .005f), can.IsTriggered ? .6f + .4f * warning : 0f);
            _body.EnableKeyword("_EMISSION");
            _body.SetColor("_EmissionColor", can.IsTriggered ? Color.red * (1f + warning * 2f) : Color.black);
            _spark.localScale = Vector3.one * (.09f + .02f * Mathf.Sin(tick * .3f));
        }

        private Material Material(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
            _materials.Add(material);
            return material;
        }

        private Transform Part(string label, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = label; part.transform.SetParent(transform, false);
            part.transform.localPosition = position; part.transform.localScale = scale;
            DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }

        private void OnDestroy()
        {
            foreach (var material in _materials)
                if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}

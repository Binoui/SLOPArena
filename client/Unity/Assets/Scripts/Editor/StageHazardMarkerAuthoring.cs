#if UNITY_EDITOR
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.StageHazards;
using UnityEngine;

namespace SlopArena.EditorTools
{
    internal static class StageHazardMarkerAuthoring
    {
        private static Vector3 Vector(JToken p) => new((float)p[0], (float)p[1], (float)p[2]);
        private static JArray Point(Vector3 p) => new(p.x, p.y, p.z);

        internal static void Apply(Transform aids, JArray specs, ArenaDefinition arena)
        {
            StageHazardAuthoring.Parse(new JObject { ["hazardAnchors"] = specs }.ToString(), arena);
            var root = aids.Find("HazardAnchors");
            if (root == null) { root = new GameObject("HazardAnchors").transform; root.SetParent(aids, false); }
            foreach (Transform child in root.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            foreach (var spec in specs)
            {
                var marker = new GameObject((string)spec["name"]).AddComponent<StageHazardAnchorMarker>();
                marker.transform.SetParent(root, false);
                marker.Kind = (StageHazardAnchorKind)Enum.Parse(typeof(StageHazardAnchorKind), (string)spec["kind"]);
                if (spec["position"] != null) marker.transform.position = Vector(spec["position"]);
                if (spec["center"] != null) marker.transform.position = Vector(spec["center"]);
                if (spec["halfExtents"] != null) marker.HalfExtents = Vector(spec["halfExtents"]);
                if (spec["yaw"] != null) marker.transform.rotation = Quaternion.Euler(0, (float)spec["yaw"] * Mathf.Rad2Deg, 0);
                if (spec["radius"] != null) marker.Radius = (float)spec["radius"];
                var points = spec["points"] as JArray;
                if (marker.Kind == StageHazardAnchorKind.Capsule) points = new JArray(spec["a"].DeepClone(), spec["b"].DeepClone());
                if (points == null) continue;
                for (int i = 0; i < points.Count; i++)
                {
                    var point = new GameObject($"Point_{i:000}").transform;
                    point.SetParent(marker.transform, false); point.position = Vector(points[i]);
                }
            }
        }

        internal static StageHazardAnchors Read(Transform aids, ArenaDefinition arena)
        {
            var root = aids.Find("HazardAnchors");
            if (root == null || root.childCount == 0) return null;
            var entries = new JArray();
            foreach (Transform child in root)
            {
                var marker = child.GetComponent<StageHazardAnchorMarker>();
                if (marker == null) throw new ArgumentException("Hazard anchor needs a StageHazardAnchorMarker: " + child.name);
                if (child.lossyScale != Vector3.one || Mathf.Abs(Mathf.DeltaAngle(child.eulerAngles.x, 0)) > .001f
                    || Mathf.Abs(Mathf.DeltaAngle(child.eulerAngles.z, 0)) > .001f)
                    throw new ArgumentException("Hazard markers require unit scale and yaw-only rotation.");
                var entry = new JObject { ["name"] = child.name, ["kind"] = marker.Kind.ToString() };
                switch (marker.Kind)
                {
                    case StageHazardAnchorKind.Point: case StageHazardAnchorKind.Origin: entry["position"] = Point(child.position); break;
                    case StageHazardAnchorKind.Box:
                        entry["center"] = Point(child.position); entry["halfExtents"] = Point(marker.HalfExtents);
                        entry["yaw"] = child.eulerAngles.y * Mathf.Deg2Rad; break;
                    case StageHazardAnchorKind.Capsule:
                        if (child.childCount != 2) throw new ArgumentException("Capsule needs exactly two child points.");
                        entry["a"] = Point(child.GetChild(0).position); entry["b"] = Point(child.GetChild(1).position); entry["radius"] = marker.Radius; break;
                    default:
                        entry["points"] = new JArray(child.Cast<Transform>().Select(p => Point(p.position)));
                        if (marker.Kind == StageHazardAnchorKind.Cells) entry["radius"] = marker.Radius; break;
                }
                entries.Add(entry);
            }
            return StageHazardAuthoring.Parse(new JObject { ["hazardAnchors"] = entries }.ToString(), arena);
        }
    }
}
#endif

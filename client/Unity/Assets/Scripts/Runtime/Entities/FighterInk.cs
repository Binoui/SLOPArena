using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Entities
{
    /// <summary>
    /// Meshes for the fighter ink line. GraphicCharacter's Ink pass pushes back faces out along the vertex
    /// tangents; here a copy of a fighter mesh gets smoothed normals written into them (w = 0 marks them), so a
    /// flat-shaded mesh keeps a closed line instead of one that splits at every hard edge. The source mesh must
    /// be readable (Read/Write on its model import); a mesh that is not keeps its tangents and draws no line.
    /// </summary>
    public static class FighterInk
    {
        // Vertices closer than a twentieth of a millimetre are one corner of the model.
        private const float Weld = 20000f;

        private static readonly Dictionary<int, Mesh> Inked = new();   // source mesh → its inked copy
        private static readonly HashSet<int> Copies = new();
        private static readonly HashSet<int> Refused = new();          // sources that cannot carry a line, reported once

        // Entering Play Mode without a domain reload keeps statics; a reimported model must not meet an old copy.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlay()
        {
            foreach (var copy in Inked.Values)
                if (copy != null) Object.Destroy(copy);
            Inked.Clear();
            Copies.Clear();
            Refused.Clear();
        }

        /// <summary>Swap the skin's mesh for its inked copy. Returns false when the mesh cannot carry a line.</summary>
        public static bool Apply(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            if (mesh == null) return false;
            if (Copies.Contains(mesh.GetInstanceID())) return true;
            var inked = For(mesh);
            if (inked == null) return false;
            skin.sharedMesh = inked;
            return true;
        }

        /// <summary>Destroy the inked copy made for <paramref name="source"/>, for meshes that leave mid-match (a model swap).</summary>
        public static void Release(Mesh source)
        {
            if (source == null) return;
            int id = source.GetInstanceID();
            Refused.Remove(id);
            if (!Inked.Remove(id, out var copy) || copy == null) return;
            Copies.Remove(copy.GetInstanceID());
            Object.Destroy(copy);
        }

        private static Mesh? For(Mesh source)
        {
            int id = source.GetInstanceID();
            if (Inked.TryGetValue(id, out var done) && done != null) return done;
            if (Refused.Contains(id)) return null;
            if (!source.isReadable)
            {
                Debug.LogWarning($"[FighterInk] {source.name} has no ink line: its mesh is not readable. Enable Read/Write on the model import.");
                Refused.Add(id);
                return null;
            }

            var vertices = source.vertices;
            var normals = source.normals;
            if (normals.Length != vertices.Length)
            {
                Refused.Add(id);
                return null;
            }
            var sums = new Dictionary<Vector3Int, Vector3>(vertices.Length);
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var p = vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * Weld), Mathf.RoundToInt(p.y * Weld), Mathf.RoundToInt(p.z * Weld));
                keys[i] = key;
                sums.TryGetValue(key, out var sum);
                sums[key] = sum + normals[i];
            }
            var tangents = new Vector4[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var smooth = sums[keys[i]];
                smooth = smooth.sqrMagnitude > 1e-10f ? smooth.normalized : normals[i];
                tangents[i] = new Vector4(smooth.x, smooth.y, smooth.z, 0f);
            }

            var copy = Object.Instantiate(source);
            copy.name = source.name + " (ink)";
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.tangents = tangents;
            Inked[id] = copy;
            Copies.Add(copy.GetInstanceID());
            return copy;
        }
    }
}

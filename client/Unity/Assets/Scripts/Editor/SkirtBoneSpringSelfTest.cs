using System;
using System.Linq;
using SlopArena.Client.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>SkirtBoneSpring's Clearance lift on a synthetic ring of hanging chains and one limb capsule.</summary>
public static class SkirtBoneSpringSelfTest
{
    private const int Chains = 8, Segments = 3;
    private const float Ring = .15f, Pivot = 1f, Segment = .1f, LimbRadius = .04f;

    [MenuItem("Tools/SLOPArena/Tests/Skirt Bone Spring Clearance")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the skirt spring check in Edit Mode.");
        using (var rig = new Rig(new Vector3(2f, 0f, 2f), new Vector3(2f, .5f, 2f)))
        {
            rig.Settle();
            for (int c = 0; c < Chains; c++)
                Require(Vector3.Distance(rig.Hem(c), rig.RestHem(c)) < 1e-4f, $"chain {c} left its rest with no limb near it");
        }
        // A forearm across under the front chain, 12 cm below its pivot and 5 mm inside it: the chain swings out just
        // clear of it, further than the collision pass alone pushes it.
        using (var rig = new Rig(new Vector3(Ring - .005f, Pivot - .12f, -.3f), new Vector3(Ring - .005f, Pivot - .12f, .3f)))
        {
            var mapped = rig.MappedPose();
            rig.Settle();
            float clearance = rig.Joints(0).Zip(rig.Joints(0).Skip(1), (a, b) => Distance(a, b, rig.LimbStart.position, rig.LimbEnd.position)).Min();
            Require(clearance >= LimbRadius, $"the front chain stays {clearance:0.000} m from a {LimbRadius} m limb");
            // Clearing the 7 cm lift reach there takes a 33-degree swing: the 30 cm chain's hem moves 16.4 cm out and
            // 4.9 cm up. Pushed only clear of the 4 cm limb, it would move 8.7 cm out.
            Vector3 moved = rig.Hem(0) - rig.RestHem(0);
            Require(moved.x > .14f && moved.y > .035f, $"the front chain swings out over the limb (hem moved {moved:F3})");
            Require(Vector3.Distance(rig.Hem(Chains / 2), rig.RestHem(Chains / 2)) < 1e-3f, "the far chain stays at rest");
            Require(mapped.SequenceEqual(rig.MappedPose()), "the spring wrote a mapped bone");
        }
        // A forearm across 9 cm under the pivot, in the top third of the fabric, which rides the body: it is only
        // pushed clear within the spring's 20-degree limit (hem under 13 cm away), not swung 48 degrees out (22 cm).
        using (var rig = new Rig(new Vector3(Ring - .005f, Pivot - .09f, -.3f), new Vector3(Ring - .005f, Pivot - .09f, .3f)))
        {
            rig.Settle();
            float moved = Vector3.Distance(rig.Hem(0), rig.RestHem(0));
            Require(moved < .13f, $"a limb beside the pivot swung the chain out {moved:0.000} m");
        }
        // A limb hanging along the chain, 1 cm inside it: swinging is no answer to it, so the fabric is only pushed
        // clear within the 20-degree limit (hem under 13 cm away) instead of swinging 33 degrees out (16.5 cm).
        using (var rig = new Rig(new Vector3(Ring - .01f, Pivot - .11f, 0f), new Vector3(Ring - .01f, Pivot - .29f, 0f)))
        {
            rig.Settle();
            float moved = Vector3.Distance(rig.Hem(0), rig.RestHem(0));
            Require(moved < .13f, $"a limb along the chain flipped it out {moved:0.000} m");
        }
        // A forearm 3 cm in front of the front chain, inside its reach: the fabric stays behind it, neither lifted
        // over it nor pushed out through it.
        using (var rig = new Rig(new Vector3(Ring + .03f, Pivot - .15f, -.3f), new Vector3(Ring + .03f, Pivot - .15f, .3f)))
        {
            rig.Settle();
            Require(Vector3.Distance(rig.Hem(0), rig.RestHem(0)) < 1e-3f, "a limb in front of the fabric moved it");
        }
        Debug.Log("[SkirtBoneSpringSelfTest] Passed: chains rest with no limb near, swing just clear of a limb under " +
            "them without moving the far side or mapped bones, and are only pushed by a limb beside their pivot or " +
            "hanging along them, never by one in front of them.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[SkirtBoneSpringSelfTest] " + message);
    }

    private static float Distance(Vector3 p1, Vector3 q1, Vector3 a, Vector3 b)
    {
        float best = float.MaxValue;
        for (int i = 0; i <= 20; i++)
        {
            Vector3 p = Vector3.Lerp(p1, q1, i / 20f), axis = b - a;
            best = Mathf.Min(best, Vector3.Distance(p, a + axis * Mathf.Clamp01(Vector3.Dot(p - a, axis) / axis.sqrMagnitude)));
        }
        return best;
    }

    private sealed class Rig : IDisposable
    {
        private readonly Scene _scene;
        private readonly Mesh _mesh;
        private readonly Transform[][] _chains = new Transform[Chains][];
        private readonly Vector3[] _restHem = new Vector3[Chains], _hemLocal = new Vector3[Chains];
        private readonly SkirtBoneSpring _spring;
        public readonly Transform Root, Frame, LimbStart, LimbEnd;

        public Rig(Vector3 limbStart, Vector3 limbEnd)
        {
            _scene = EditorSceneManager.NewPreviewScene();
            Root = new GameObject("Skirt spring rig").transform;
            SceneManager.MoveGameObjectToScene(Root.gameObject, _scene);
            Frame = Child("Frame", Root, new Vector3(0f, Pivot, 0f));
            LimbStart = Child("Limb", Root, limbStart);
            LimbEnd = Child("LimbEnd", LimbStart, limbEnd - limbStart);
            for (int c = 0; c < Chains; c++)
            {
                float angle = c * 2f * Mathf.PI / Chains;
                var parent = Frame;
                var at = new Vector3(Ring * Mathf.Cos(angle), 0f, Ring * Mathf.Sin(angle));
                _chains[c] = new Transform[Segments];
                for (int j = 0; j < Segments; j++)
                {
                    _chains[c][j] = Child($"Chain_{c}_{j}", parent, j == 0 ? at : Vector3.down * Segment);
                    parent = _chains[c][j];
                }
                // The spring extends the last bone by its parent's length; the hem rides that bone.
                _restHem[c] = parent.position + Vector3.down * Segment;
                _hemLocal[c] = parent.InverseTransformPoint(_restHem[c]);
            }
            var fabric = new GameObject("Fabric");
            fabric.transform.SetParent(Root, false);
            var bones = _chains.SelectMany(chain => chain).ToArray();
            _mesh = new Mesh
            {
                vertices = bones.Select(b => b.position).ToArray(),
                boneWeights = Enumerable.Range(0, bones.Length).Select(i => new BoneWeight { boneIndex0 = i, weight0 = 1f }).ToArray(),
                bindposes = bones.Select(b => b.worldToLocalMatrix).ToArray(),
            };
            var skin = fabric.AddComponent<SkinnedMeshRenderer>();
            skin.bones = bones;
            skin.sharedMesh = _mesh;
            _spring = fabric.AddComponent<SkirtBoneSpring>();
            var settings = new SerializedObject(_spring);
            settings.FindProperty("_hips").objectReferenceValue = Frame;
            settings.FindProperty("_modelRoot").objectReferenceValue = Root;
            var chains = settings.FindProperty("_chains");
            chains.arraySize = Chains;
            for (int c = 0; c < Chains; c++)
            {
                var chain = chains.GetArrayElementAtIndex(c).FindPropertyRelative("Bones");
                chain.arraySize = Segments;
                for (int j = 0; j < Segments; j++) chain.GetArrayElementAtIndex(j).objectReferenceValue = _chains[c][j];
            }
            var capsules = settings.FindProperty("_capsules");
            capsules.arraySize = 1;
            capsules.GetArrayElementAtIndex(0).FindPropertyRelative("Start").objectReferenceValue = LimbStart;
            capsules.GetArrayElementAtIndex(0).FindPropertyRelative("End").objectReferenceValue = LimbEnd;
            capsules.GetArrayElementAtIndex(0).FindPropertyRelative("Radius").floatValue = LimbRadius;
            settings.FindProperty("_liftModel").enumValueIndex = (int)SkirtBoneSpring.LiftModel.Clearance;
            settings.FindProperty("_fabricClearance").floatValue = 0f;
            settings.FindProperty("_liftReach").floatValue = .03f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform Child(string name, Transform parent, Vector3 localPosition)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            child.localPosition = localPosition;
            return child;
        }

        public void Settle()
        {
            _spring.Step(0f);
            for (int frame = 0; frame < 60; frame++) _spring.Step(1f / 60f);
        }

        /// <summary>The chain's joints, ending with its extrapolated hem.</summary>
        public Vector3[] Joints(int c) => _chains[c].Select(b => b.position).Append(Hem(c)).ToArray();

        public Vector3 Hem(int c) => _chains[c][Segments - 1].TransformPoint(_hemLocal[c]);

        public Vector3 RestHem(int c) => _restHem[c];

        public string[] MappedPose() => new[] { Frame, LimbStart, LimbEnd }
            .Select(t => $"{t.name} {t.localPosition:F6} {t.localRotation:F6}").ToArray();

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(_mesh);
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}

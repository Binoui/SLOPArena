using System;
using System.Linq;
using SlopArena.Client.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>SkirtBoneSpring's Clearance lift and its hang on a synthetic ring of hanging chains and one limb capsule.</summary>
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
        // Hang: the body leans 30 degrees toward chain 0. That chain falls away from the body and hangs plumb; the
        // chain on the far side would swing into the body, so it keeps its rest line in the leaning frame.
        var far = new Vector3(2f, 0f, 2f);
        using (var rig = new Rig(far, far + Vector3.up * .5f, hang: 1f))
        {
            rig.Settle();
            rig.Lean(30f);
            rig.Settle();
            float offPlumb = Vector3.Angle(rig.Hem(0) - rig.PivotOf(0), Vector3.down);
            Require(offPlumb < 2f, $"the chain on the low side hangs {offPlumb:0.0} degrees off plumb");
            Require(Vector3.Distance(rig.Hem(Chains / 2), rig.LeaningRestHem(Chains / 2)) < 1e-3f, "the chain on the high side left the body");
        }
        // A body lying flat: the swing stops at the 40-degree limit instead of wrapping the fabric around it.
        using (var rig = new Rig(far, far + Vector3.up * .5f, hang: 1f))
        {
            rig.Settle();
            rig.Lean(90f);
            rig.Settle();
            float swing = Vector3.Angle(rig.Hem(0) - rig.PivotOf(0), rig.LeaningRestHem(0) - rig.PivotOf(0));
            Require(Mathf.Abs(swing - 40f) < 2f, $"a flat body's low chain swings {swing:0.0} degrees, not the 40-degree limit");
        }
        // No hang set: a lean carries every chain with the frame, as before the option existed.
        using (var rig = new Rig(far, far + Vector3.up * .5f))
        {
            rig.Settle();
            rig.Lean(30f);
            rig.Settle();
            for (int c = 0; c < Chains; c++)
                Require(Vector3.Distance(rig.Hem(c), rig.LeaningRestHem(c)) < 1e-3f, $"chain {c} hangs with no hang set");
        }
        // LegPose with a short coat's values: a thigh under chain 0, raised level toward it, lifts that chain about
        // (0 + 80) / 125 * 150 = 96 degrees. Chain 1 faces the thigh too but hangs 10.6 cm to its side: a closed
        // skirt lifts it as well, an open coat's panel width (5 cm here) leaves it down. The far side never moves.
        foreach (float width in new[] { 0f, .05f })
            using (var rig = new Rig(new Vector3(.02f, Pivot - .05f, 0f), new Vector3(.42f, Pivot - .05f, 0f), configure: settings =>
            {
                settings.FindProperty("_liftModel").enumValueIndex = (int)SkirtBoneSpring.LiftModel.LegPose;
                settings.FindProperty("_legLiftFrom").floatValue = -80f;
                settings.FindProperty("_legLiftFull").floatValue = 45f;
                settings.FindProperty("_legLift").floatValue = 150f;
                settings.FindProperty("_legLiftWidth").floatValue = width;
            }))
            {
                rig.Settle();
                float front = Vector3.Angle(rig.Hem(0) - rig.PivotOf(0), Vector3.down);
                float beside = Vector3.Angle(rig.Hem(1) - rig.PivotOf(1), Vector3.down);
                Require(front > 80f && front < 112f, $"a level thigh lifts its chain {front:0.0} degrees, not about 96 (width {width})");
                Require(width > 0f ? beside < 25f : beside > 60f, $"the chain 10.6 cm beside the thigh lifts {beside:0.0} degrees at panel width {width}");
                Require(Vector3.Distance(rig.Hem(Chains / 2), rig.RestHem(Chains / 2)) < 1e-3f, "the far chain left its rest under a raised thigh");
            }
        Debug.Log("[SkirtBoneSpringSelfTest] Passed: chains rest with no limb near, swing just clear of a limb under " +
            "them without moving the far side or mapped bones, and are only pushed by a limb beside their pivot or " +
            "hanging along them, never by one in front of them. With hang, the low side of a leaning body hangs " +
            "plumb up to the limit and the high side stays on the body. With LegPose's coat values a level thigh " +
            "lifts its own chain about 96 degrees and, at a panel width, not the chain beside it.");
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
        private readonly Vector3[] _restHem = new Vector3[Chains], _hemLocal = new Vector3[Chains], _restHemFrame = new Vector3[Chains];
        private readonly SkirtBoneSpring _spring;
        public readonly Transform Root, Frame, LimbStart, LimbEnd;

        public Rig(Vector3 limbStart, Vector3 limbEnd, float hang = 0f, Action<SerializedObject> configure = null)
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
                _restHemFrame[c] = Frame.InverseTransformPoint(_restHem[c]);
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
            settings.FindProperty("_hang").floatValue = hang;
            configure?.Invoke(settings);
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

        /// <summary>Tips the frame toward chain 0 (+X), as a body leaning that way.</summary>
        public void Lean(float degrees) => Frame.localRotation = Quaternion.AngleAxis(-degrees, Vector3.forward);

        public Vector3 PivotOf(int c) => _chains[c][0].position;

        /// <summary>Where the chain's hem is when it keeps its rest line in the leaning frame.</summary>
        public Vector3 LeaningRestHem(int c) => Frame.TransformPoint(_restHemFrame[c]);

        public string[] MappedPose() => new[] { Frame, LimbStart, LimbEnd }
            .Select(t => $"{t.name} {t.localPosition:F6} {t.localRotation:F6}").ToArray();

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(_mesh);
            EditorSceneManager.ClosePreviewScene(_scene);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using SlopArena.Client.Entities;
using SlopArena.Shared;

internal static class DeterministicPoseTrackBaker
{
    internal static readonly HumanBodyBones[] RequiredBones =
    {
        HumanBodyBones.Head,
        HumanBodyBones.UpperChest,
        HumanBodyBones.Hips,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightFoot,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightToes,
        HumanBodyBones.LeftToes,
    };

    internal sealed class SampledAnimation
    {
        public string SemanticId = "";
        public string PoseTrackId = "";
        public AnimationClip Clip = null!;
        public int FrameCount;
        public byte[] Bytes = Array.Empty<byte>();
    }

    /// <summary>
    /// Bake hips-relative points for every clip. With <paramref name="modelYOffset"/>, also
    /// write <see cref="BakedAnimationData.HipsOffsetPointName"/>: the drawn hips relative to
    /// the capsule centre, so Shared tests every point where the renderer draws it.
    /// </summary>
    internal static byte[] Bake(
        GameObject rig,
        IReadOnlyList<SampledAnimation> animations,
        int sampleRate,
        WeaponAttachConfig weaponConfig = null,
        float visualScale = 1f,
        float? modelYOffset = null)
    {
        if (sampleRate != 60) throw new InvalidOperationException("Sample rate must be exactly 60 Hz.");
        if (rig == null) throw new InvalidOperationException("Rig is missing.");
        if (animations == null || animations.Count == 0) throw new InvalidOperationException("No animations to bake.");
        if (!IsPositiveFinite(visualScale)) throw new InvalidOperationException("Visual scale must be finite and positive.");
        if (modelYOffset.HasValue && !IsFinite(modelYOffset.Value))
            throw new InvalidOperationException("Model Y offset must be finite.");

        var sourceAnimator = rig.GetComponent<Animator>();
        if (sourceAnimator == null) throw new InvalidOperationException("Rig has no Animator.");
        var temp = UnityEngine.Object.Instantiate(rig);
        temp.name = $"{rig.name}_CharacterCookTemp";
        temp.hideFlags = HideFlags.HideAndDontSave;
        // PlayerRenderer parents the rig at the model root with identity rotation and
        // replaces its scale with visualScale; sample in that frame so every point
        // matches the rendered model.
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one * visualScale;
        try
        {
            var animator = temp.GetComponent<Animator>();
            if (animator == null) throw new InvalidOperationException("Cloned rig has no Animator.");
            // Evaluate clips as Animancer plays them: a playable graph without root motion
            // or foot IK. AnimationClip.SampleAnimation agrees on the hips-relative pose but
            // places the hips elsewhere for clips with root curves (up to 3.5 m off).
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents = false;
            animator.Rebind();
            var transforms = new Transform[RequiredBones.Length];
            int weaponPoints = weaponConfig == null ? 0 : 2;
            var names = new string[RequiredBones.Length + weaponPoints + (modelYOffset.HasValue ? 1 : 0)];
            for (int i = 0; i < RequiredBones.Length; i++)
            {
                transforms[i] = animator.GetBoneTransform(RequiredBones[i]);
                if (transforms[i] == null)
                    throw new InvalidOperationException($"Required humanoid bone is missing: {RequiredBones[i]}.");
                names[i] = transforms[i].name;
            }
            WeaponEntry weaponEntry = null;
            Transform weaponBone = null;
            Vector3 tipLocal = Vector3.zero;
            Vector3 hiltLocal = Vector3.zero;
            if (weaponConfig != null)
            {
                weaponEntry = Array.Find(weaponConfig.Entries ?? Array.Empty<WeaponEntry>(), x => x != null);
                if (weaponEntry == null) throw new InvalidOperationException("Weapon config has no entries.");
                weaponBone = Array.Find(temp.GetComponentsInChildren<Transform>(true),
                    x => x.name == weaponEntry.BoneName);
                if (weaponBone == null) throw new InvalidOperationException($"Weapon config bone is missing: {weaponEntry.BoneName}.");
                if (weaponEntry.Prefab == null) throw new InvalidOperationException($"Weapon prefab is missing for bone {weaponEntry.BoneName}.");
                Transform root = weaponEntry.Prefab.transform;
                var weaponTransforms = weaponEntry.Prefab.GetComponentsInChildren<Transform>(true);
                Transform hiltMarker = weaponTransforms.SingleOrDefault(x => x.name == "bladeHilt");
                Transform tipMarker = weaponTransforms.SingleOrDefault(x => x.name == "bladeEnd");
                if (hiltMarker != null || tipMarker != null)
                {
                    if (hiltMarker == null || tipMarker == null)
                        throw new InvalidOperationException($"Weapon '{root.name}' requires both bladeHilt and bladeEnd markers.");
                    hiltLocal = Vector3.Scale(root.localScale, root.InverseTransformPoint(hiltMarker.position));
                    tipLocal = Vector3.Scale(root.localScale, root.InverseTransformPoint(tipMarker.position));
                }
                else
                {
                    var vertices = new List<Vector3>();
                    foreach (var meshFilter in weaponEntry.Prefab.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = meshFilter.sharedMesh;
                        if (mesh == null || !mesh.isReadable) continue;
                        Matrix4x4 toPrefab = root.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix;
                        foreach (var vertex in mesh.vertices)
                            vertices.Add(toPrefab.MultiplyPoint3x4(vertex));
                    }
                    if (vertices.Count > 0)
                    {
                        Vector3 min = vertices[0], max = vertices[0];
                        foreach (var vertex in vertices) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
                        Vector3 extent = max - min;
                        Vector3 axis = extent.x >= extent.y && extent.x >= extent.z ? Vector3.right
                            : extent.y >= extent.z ? Vector3.up : Vector3.forward;
                        float tipProjection = float.MinValue, hiltProjection = float.MaxValue;
                        foreach (var vertex in vertices)
                        {
                            float projection = Vector3.Dot(vertex, axis);
                            if (projection > tipProjection) { tipProjection = projection; tipLocal = vertex; }
                            if (projection < hiltProjection) { hiltProjection = projection; hiltLocal = vertex; }
                        }
                        hiltLocal = Vector3.Scale(root.localScale, hiltLocal);
                        tipLocal = Vector3.Scale(root.localScale, tipLocal);
                    }
                    else
                    {
                        // Readability is optional for presentation prefabs (e.g. non-blade props like bombs).
                        // Keep deterministic fallback points when an imported mesh does not expose CPU vertices.
                        tipLocal = new Vector3(0f, 0f, 1.5f);
                        hiltLocal = Vector3.zero;
                    }
                }
                names[RequiredBones.Length] = "_weapon_tip";
                names[RequiredBones.Length + 1] = "_weapon_hilt";
            }
            if (modelYOffset.HasValue)
                names[RequiredBones.Length + weaponPoints] = BakedAnimationData.HipsOffsetPointName;
            var hips = transforms[2];
            using var stream = new MemoryStream();
            WriteUInt32(stream, 0x4C454B53u);
            WriteUInt32(stream, 1u);
            WriteUInt32(stream, (uint)names.Length);
            WriteUInt32(stream, (uint)animations.Count);
            foreach (string name in names) WriteString(stream, name);

            foreach (var animation in animations.OrderBy(x => x.PoseTrackId, StringComparer.Ordinal))
            {
                if (animation.Clip == null || animation.FrameCount <= 0)
                    throw new InvalidOperationException($"Animation '{animation.SemanticId}' has no valid frames.");
                WriteString(stream, animation.PoseTrackId);
                WriteUInt32(stream, (uint)animation.FrameCount);
                var graph = PlayableGraph.Create(temp.name);
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var output = AnimationPlayableOutput.Create(graph, "pose", animator);
                    var playable = AnimationClipPlayable.Create(graph, animation.Clip);
                    playable.SetApplyFootIK(false);
                    output.SetSourcePlayable(playable);
                    graph.Play();
                    for (int frame = 0; frame < animation.FrameCount; frame++)
                    {
                        playable.SetTime(frame / 60d);
                        graph.Evaluate(0f);
                        Vector3 hipsPosition = hips.position;
                        for (int bone = 0; bone < transforms.Length; bone++)
                            WriteFiniteVector(stream, transforms[bone].position - hipsPosition, animation.SemanticId, frame, names[bone]);
                        if (weaponEntry != null)
                        {
                            Vector3 bladePosition = weaponBone.TransformPoint(weaponEntry.PositionOffset);
                            Quaternion bladeRotation = weaponBone.rotation * Quaternion.Euler(weaponEntry.RotationOffset);
                            Vector3 tip = bladePosition + bladeRotation * tipLocal;
                            Vector3 hilt = bladePosition + bladeRotation * hiltLocal;
                            WriteFiniteVector(stream, tip - hipsPosition, animation.SemanticId, frame, "_weapon_tip");
                            WriteFiniteVector(stream, hilt - hipsPosition, animation.SemanticId, frame, "_weapon_hilt");
                        }
                        // The model root sits modelYOffset above the capsule centre.
                        if (modelYOffset.HasValue)
                            WriteFiniteVector(stream, hipsPosition + Vector3.up * modelYOffset.Value,
                                animation.SemanticId, frame, BakedAnimationData.HipsOffsetPointName);
                    }
                }
                finally
                {
                    graph.Destroy();
                }
            }
            return stream.ToArray();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(temp);
        }
    }

    private static bool IsFinite(float value)
        => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsPositiveFinite(float value)
        => IsFinite(value) && value > 0f;


    private static void WriteFiniteVector(Stream stream, Vector3 value, string semanticId, int frame, string bone)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
            float.IsNaN(value.y) || float.IsInfinity(value.y) ||
            float.IsNaN(value.z) || float.IsInfinity(value.z))
            throw new InvalidOperationException($"Sampled non-finite pose for '{semanticId}', frame {frame}, bone '{bone}'.");
        WriteSingle(stream, value.x);
        WriteSingle(stream, value.y);
        WriteSingle(stream, value.z);
    }

    private static Transform FindMarker(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(x => x.name == name);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteSingle(Stream stream, float value)
        => WriteUInt32(stream, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

    private static void WriteUInt32(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }
}

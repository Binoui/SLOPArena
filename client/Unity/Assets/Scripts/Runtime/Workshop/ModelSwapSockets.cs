#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// Where a swapped model holds the built-in fighter's weapons. The weapon rule (ADR-0065) places a prop at its
    /// bone with the prop's own scale and offsets authored in that bone's space. A creator's bones have their own roll
    /// and scale, so each weapon bone gets a socket on the swapped rig: under the matching human bone, turned and
    /// scaled as the built-in bone is when both models hold the same pose. A helper bone that is not a human bone
    /// (Sir Bonk's pauldron) keeps its offset from the nearest human bone above it.
    /// </summary>
    public static class ModelSwapSockets
    {
        /// <summary>Both animators must hold the same humanoid pose, facing the same way, at the same height.</summary>
        public static Dictionary<string, Transform> Build(Animator builtIn, Animator swap, IEnumerable<string> boneNames)
        {
            var humanOf = new Dictionary<Transform, HumanBodyBones>();
            for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                if (builtIn.GetBoneTransform(bone) is { } t) humanOf.TryAdd(t, bone);
            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var t in builtIn.GetComponentsInChildren<Transform>(true)) byName.TryAdd(t.name, t);

            var sockets = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (string name in boneNames)
            {
                if (sockets.ContainsKey(name) || !byName.TryGetValue(name, out var source)) continue;
                Transform? anchor = source;
                HumanBodyBones human = HumanBodyBones.LastBone;
                while (anchor != null && !humanOf.TryGetValue(anchor, out human)) anchor = anchor.parent;
                var target = anchor != null ? swap.GetBoneTransform(human) : null;
                if (anchor == null || target == null) continue;
                var socket = new GameObject(name + " (model swap socket)").transform;
                socket.SetParent(target, false);
                socket.SetPositionAndRotation(target.position + (source.position - anchor.position), source.rotation);
                Vector3 want = source.lossyScale, have = target.lossyScale;
                socket.localScale = new Vector3(Ratio(want.x, have.x), Ratio(want.y, have.y), Ratio(want.z, have.z));
                sockets.Add(name, socket);
            }
            return sockets;
        }

        private static float Ratio(float want, float have) => Mathf.Abs(have) > 1e-8f ? want / have : 1f;
    }
}

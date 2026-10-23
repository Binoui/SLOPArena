using SlopArena.EditorTools;
using System;
using System.Linq;
using SlopArena.Client.Entities;
using SlopArena.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SwordTrailPresentationSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Sword Trail Presentation")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the isolated sword trail check in Edit Mode.");

        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Sword trail regression") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(root, scene);
        var style = new GameObject("Sword trail style") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(style, scene);
        var prefab = new GameObject("Sword trail weapon") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(prefab, scene);
        var material = new Material(Shader.Find("Sprites/Default"));
        var config = ScriptableObject.CreateInstance<WeaponAttachConfig>();
        try
        {
            new GameObject("hilt").transform.SetParent(prefab.transform, false);
            var bladeTip = new GameObject("tip").transform;
            bladeTip.SetParent(prefab.transform, false);
            bladeTip.localPosition = Vector3.forward;
            var bone = new GameObject("Sword trail bone").transform;
            bone.SetParent(root.transform, false);
            root.AddComponent<SkinnedMeshRenderer>().bones = new[] { bone };
            var particles = style.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            style.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            config.Entries = new[]
            {
                new WeaponEntry
                {
                    AttackSlot = AbilitySlots.Slot1,
                    BoneName = bone.name,
                    Prefab = prefab,
                    TrailStylePrefab = style,
                    HitboxMotionTime = 0.12f,
                    TrailBladeWidth = 1f,
                    TrailHiltAnchor = "hilt",
                    TrailTipAnchor = "tip",
                },
            };
            var renderer = root.AddComponent<PlayerRenderer>();
            var definition = new CharacterDefinition
            {
                CookedSlots = new[] { PresentationSelfTestContent.Move(0,
                    PresentationSelfTestContent.Stage(12, Array.Empty<string>(),
                        new HitboxEvent { TriggerTick = 2, DurationTicks = 2,
                            BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip" })) },
            };
            var baked = new BakedAnimationData
            {
                BoneNames = Array.Empty<string>(),
                Animations = Array.Empty<BakedAnimationData.BakedAnim>(),
            };
            Require(renderer.Configure(definition, baked, null, null, out var error), error);
            var weapon = root.AddComponent<WeaponAttach>();
            weapon.Init(renderer, config);
            var meshFilter = root.GetComponentsInChildren<MeshFilter>(true)
                .Single(filter => filter.name == "TECH blade sweep root");
            Mesh mesh = meshFilter.sharedMesh;
            var state = new CharacterState
            {
                State = ActionState.Attacking,
                AttackSlot = AbilitySlots.Slot1,
                IsGrounded = true,
            };
            Vector3 tip = default;
            void Present(ushort elapsed, ushort hitstop = 0, float offset = 0f)
            {
                state.AttackElapsedTicks = elapsed;
                state.HitstopTicks = hitstop;
                Vector3 hilt = new(offset + elapsed * 0.1f, 0f, 0f);
                tip = hilt + Vector3.forward;
                weapon.CaptureState(state, false, state.State == ActionState.Attacking, hilt, tip);
                // A late empty authoritative hitbox snapshot must not cut off a baked swing.
                weapon.SetHitboxTrailActive(false);
                weapon.RefreshPresentation();
            }
            Present(1);
            Require(mesh.vertexCount == 0, "Sword emitted before the authored blade window.");
            Present(2);
            Present(3);
            Present(4, 7);
            Require((meshFilter.transform.TransformPoint(mesh.vertices.Last()) - tip).sqrMagnitude < 0.000001f,
                "Blade emission stopped at the damage-window end instead of following the swing.");
            var heldVertices = mesh.vertices;
            var heldDissolve = mesh.uv2;
            for (ushort freeze = 6; ; freeze--)
            {
                Present(4, freeze);
                Require(mesh.vertices.SequenceEqual(heldVertices) && mesh.uv2.SequenceEqual(heldDissolve),
                    "Hitstop aged the sword ribbon, including its final zero-tick pose.");
                if (freeze == 0) break;
            }
            Present(5);
            Require((meshFilter.transform.TransformPoint(mesh.vertices.Last()) - tip).sqrMagnitude < 0.000001f,
                "The resumed swing did not emit at its current blade tip.");

            state.State = ActionState.Hitstun;
            state.AttackSlot = 0;
            state.HitstunTicks = 10;
            Vector3 lastTip = tip;
            Present(0);
            Require(meshFilter.gameObject.activeInHierarchy && mesh.vertexCount > 0,
                "Interruption hid or cleared the fading tail with its attack-only weapon.");
            Require((meshFilter.transform.TransformPoint(mesh.vertices.Last()) - lastTip).sqrMagnitude < 0.000001f,
                "Interrupted animation kept emitting new sword geometry.");
            for (int tick = 0; tick < 9; tick++) Present(0);
            Require(mesh.vertexCount == 0, "Interrupted sword geometry failed to expire naturally.");

            state.State = ActionState.Attacking;
            state.AttackSlot = AbilitySlots.Slot1;
            state.AttackSequence++;
            Present(2, offset: 2f);
            Present(3, offset: 2f);
            Require((meshFilter.transform.TransformPoint(mesh.vertices.Last()) - tip).sqrMagnitude < 0.000001f
                && mesh.vertices.All(vertex => meshFilter.transform.TransformPoint(vertex).x >= 2f),
                "A new attack connected its ribbon to an old swing.");
            state.Deaths++;
            state.State = ActionState.Idle;
            state.AttackSlot = 0;
            Present(0);
            Require(mesh.vertexCount == 0, "Respawn retained old sword geometry.");
            Debug.Log("[SwordTrailPresentationSelfTest] Passed animation follow-through, Hitstop/final frozen pose, stale hitbox snapshots, interruption fade, attack identity and respawn reset.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(style);
            UnityEngine.Object.DestroyImmediate(prefab);
            UnityEngine.Object.DestroyImmediate(config);
            UnityEngine.Object.DestroyImmediate(material);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

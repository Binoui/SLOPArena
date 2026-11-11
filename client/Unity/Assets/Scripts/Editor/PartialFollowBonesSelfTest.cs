using System;
using SlopArena.Client.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>PartialFollowBones on a synthetic joint: a helper takes its share of the driver's turn and stays put at rest.</summary>
public static class PartialFollowBonesSelfTest
{
    [MenuItem("Tools/SLOPArena/Tests/Partial Follow Bones")]
    public static void Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Run the partial follow check in Edit Mode.");
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Partial follow rig");
            SceneManager.MoveGameObjectToScene(root, scene);
            var parent = Child("Thigh", root.transform, Quaternion.Euler(10f, 20f, 30f));
            // A rest rotation that is not identity: the share is of the turn away from rest, not of the rotation.
            Quaternion rest = Quaternion.Euler(5f, -15f, 40f);
            var driver = Child("Shin", parent, rest);
            var half = Child("KneeGuard", parent, rest);
            var none = Child("Still", parent, rest);
            var full = Child("Copy", parent, rest);
            var follow = root.AddComponent<PartialFollowBones>();
            follow.Configure(new[]
            {
                new PartialFollowBones.Follow { Helper = half, Driver = driver, Share = .5f, Rest = rest },
                new PartialFollowBones.Follow { Helper = none, Driver = driver, Share = 0f, Rest = rest },
                new PartialFollowBones.Follow { Helper = full, Driver = driver, Share = 1f, Rest = rest },
            });
            follow.Step();
            Require(Quaternion.Angle(half.localRotation, rest) < 1e-3f, "a helper left its rest while the driver was at rest");

            driver.localRotation = rest * Quaternion.AngleAxis(100f, Vector3.right);  // a knee bent 100 degrees
            follow.Step();
            float turned = Quaternion.Angle(half.localRotation, rest), left = Quaternion.Angle(half.localRotation, driver.localRotation);
            Require(Mathf.Abs(turned - 50f) < .01f && Mathf.Abs(left - 50f) < .01f,
                $"a half share turned {turned:0.00} degrees from rest and stopped {left:0.00} short of a 100-degree bend");
            Require(Quaternion.Angle(none.localRotation, rest) < 1e-3f, "a zero share moved");
            Require(Quaternion.Angle(full.localRotation, driver.localRotation) < 1e-3f, "a full share does not copy the driver");
            Require(half.localScale == Vector3.one && half.localPosition == Vector3.zero, "a helper's position or scale changed");
            Debug.Log("[PartialFollowBonesSelfTest] Passed: helpers rest with their driver, take their share of its turn " +
                "away from rest (half of a 100-degree bend is 50 degrees), and only rotate.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static Transform Child(string name, Transform parent, Quaternion localRotation)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localRotation = localRotation;
        return child;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[PartialFollowBonesSelfTest] " + message);
    }
}

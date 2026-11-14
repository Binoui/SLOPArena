using static UnityEngine.Object;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

internal static class BodyClockScene
{
    internal static IEnumerator Training(string p1, string p2, byte form, Action<TrainingMatch> ready)
    {
        Time.timeScale = 1;
        MatchConfig.Reset();
        MatchConfig.Mode = GameMode.Training;
        MatchConfig.ArenaName = "training";
        MatchConfig.StageHazards = StageHazardMode.Off;
        MatchConfig.SetPlayerFighter(p1);
        MatchConfig.Seat(2).Fighter = p2;
        MatchConfig.PlayerForm = form;
        MatchConfig.Seat(2).Form = form;
        SceneManager.LoadScene("Arena_Offline");
        yield return new WaitForSecondsRealtime(2);
        TrainingMatch match = null;
        float deadline = Time.realtimeSinceStartup + 40;
        while (Time.realtimeSinceStartup < deadline)
        {
            match = FindFirstObjectByType<TrainingMatch>();
            if (match != null && match.CaptureSequenceReady) break;
            yield return null;
        }
        if (match == null || !match.CaptureSequenceReady) throw new InvalidOperationException($"Training {p1} vs {p2} never became ready: {match?.ContentFailure}");
        ready(match);
    }

    internal static void Freeze(TrainingMatch match, float y)
    {
        var fighters = FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None).Where(r => r.CharacterDef != null).OrderBy(r => r.EntityId).ToArray();
        for (int i = 0; i < fighters.Length; i++)
        {
            var r = fighters[i];
            r.transform.SetPositionAndRotation(new Vector3(i == 0 ? -1.3f : 1.3f, y + r.ModelYOffset, 0f), Quaternion.Euler(0f, i == 0 ? 90f : -90f, 0f));
        }
        foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (behaviour.GetType().Name is "CameraMount" or "CinemachineBrain" or "ChatOverlay" or "TargetIndicator" or "TargetLockIndicator" or "GroundDestinationIndicator")
                behaviour.enabled = false;
        HideStage();
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            var data = camera.GetUniversalAdditionalCameraData();
            data.dithering = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .12f, .12f);
            camera.fieldOfView = 30f;
            camera.transform.position = new Vector3(0f, y + 1.1f, -7.5f);
            camera.transform.LookAt(new Vector3(0f, y + .8f, 0f));
        }
    }

    internal static void HideStage()
    {
        var keep = new HashSet<Renderer>(FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None).Where(r => r.CharacterDef != null)
            .SelectMany(r => r.GetComponentsInChildren<Renderer>(true)));
        foreach (var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None)) if (!keep.Contains(renderer)) renderer.enabled = false;
    }

}

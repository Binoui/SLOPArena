// Isolated Editor runner for the body pose clock change. Copy into Assets/Editor/ of a disposable
// project copy (never the canonical project) and run:
//   xvfb-run -a Unity -batchmode -quit -projectPath <copy> -executeMethod BodyClockEditorSelfTests.Run \
//     -bodyClockSelfTests <output.json> [-bodyClockSelfTest <Type.Method> ...]
// It runs the named Edit Mode self-tests that drive PlayerRenderer or the Ability Lab scenario and
// records each exception instead of stopping at the first.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BodyClockEditorSelfTests
{
    [Serializable] public sealed class Result { public string name; public bool passed; public string exception; public long milliseconds; }
    [Serializable] public sealed class Report { public bool batchMode; public string unity; public Result[] tests; }

    static readonly string[] Default =
    {
        "PlayerRendererConfigurationSelfTest.Run",
        "AnimationBehaviorSelfTest.RunExtrapolation",
        "AnimationBehaviorSelfTest.RunAttackRestart",
        "GrabAnimationPresentationSelfTest.Run",
        "BonkFAnimationPresentationSelfTest.RunCapabilityPhases",
        "BonkFAnimationPresentationSelfTest.RunLeapLandingContinuity",
        "BonkFAnimationPresentationSelfTest.Run",
        "SwordTrailPresentationSelfTest.Run",
        "CharacterAssetCookerSelfTest.RunRenderedPoseAlignmentSelfTest",
        "AbilityLabScenarioSelfTest.Run",
        "AbilityLabScenarioCommandSelfTest.Run",
    };

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-bodyClockSelfTests");
        string output = at >= 0 ? args[at + 1] : Path.GetFullPath("body-clock-selftests.json");
        var names = new List<string>();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-bodyClockSelfTest") names.Add(args[i + 1]);
        if (names.Count == 0) names.AddRange(Default);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var results = new List<Result>();
        foreach (string name in names)
        {
            var result = new Result { name = name };
            var timer = Stopwatch.StartNew();
            try
            {
                int dot = name.LastIndexOf('.');
                var type = assemblies.Select(a => a.GetType(name.Substring(0, dot))).FirstOrDefault(t => t != null)
                    ?? throw new InvalidOperationException("Type not found: " + name);
                var method = type.GetMethod(name.Substring(dot + 1), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null, Type.EmptyTypes, null) ?? throw new InvalidOperationException("Method not found: " + name);
                method.Invoke(null, null);
                result.passed = true;
            }
            catch (Exception error)
            {
                var inner = error is TargetInvocationException { InnerException: { } cause } ? cause : error;
                result.exception = inner.ToString();
                UnityEngine.Debug.LogException(inner);
            }
            result.milliseconds = timer.ElapsedMilliseconds;
            results.Add(result);
            UnityEngine.Debug.Log($"[BodyClockSelfTests] {(result.passed ? "PASS" : "FAIL")} {name} ({result.milliseconds} ms)");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonUtility.ToJson(new Report
        {
            batchMode = Application.isBatchMode, unity = Application.unityVersion, tests = results.ToArray(),
        }, true));
        if (Application.isBatchMode) EditorApplication.Exit(results.All(r => r.passed) ? 0 : 1);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Animancer;
using SlopArena.Client;
using SlopArena.Client.Entities;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using UnityEngine;

public sealed class Unit6ModesCapture : MonoBehaviour
{
    string output;
    readonly StringBuilder csv = new("fighter,form,phase,tick,state,anim,authAnim,renderFrame,authFrame,weight,point,currentMm,proposedMm\n");
    readonly StringBuilder trace = new("fighter,form,kind,renderFrame,simulationTick,elapsed,clipSeconds\n");
    static readonly MethodInfo Resolve = typeof(ServerSimulation).GetMethod("ResolveBoneAnimFrame", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly string[] Points = { "mixamorig:Head", "mixamorig:Hips", "mixamorig:Spine2", "mixamorig:LeftHand", "mixamorig:RightHand", "mixamorig:LeftFoot", "mixamorig:RightFoot" };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-unit6ModesCapture");
        if (at < 0) return;
        Directory.CreateDirectory(args[at+1]); File.WriteAllText(Path.Combine(args[at+1],"boot.txt"),"boot");
        var go = new GameObject("BodyClockProbe"); DontDestroyOnLoad(go);
        go.AddComponent<Unit6ModesCapture>().output = args[at + 1];
    }
    void Start() { File.WriteAllText(Path.Combine(output,"start.txt"),"start"); StartCoroutine(Guard(Run())); }
    IEnumerator Guard(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(body);
        while (stack.Count > 0)
        {
            object current;
            try { if (!stack.Peek().MoveNext()) { stack.Pop(); continue; } current = stack.Peek().Current; }
            catch (Exception e) { File.WriteAllText(Path.Combine(output,"failure.txt"), e.ToString()); Application.Quit(1); yield break; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        File.WriteAllText(Path.Combine(output,"done.txt"),"complete\n"); Application.Quit();
    }
    IEnumerator Run()
    {
        Directory.CreateDirectory(output);
        yield return new WaitForSecondsRealtime(15);
        foreach (var fighter in new[] { "fightguy", "manki", "wibou", "bonk", "bashie", "salamander", "nyx" })
        for (byte form = 0; form < (fighter == "nyx" ? 2 : 1); form++)
        {
            if (output.EndsWith("30") && fighter != "fightguy") continue;
            MatchConfig.PlayerForm = form;
            TrainingMatch match = null;
            yield return BodyClockScene.Training(fighter, fighter, form, m => match = m);
            var s0 = match.GetCaptureState(1); s0.Form.Active = form; match.SetCaptureState(1, s0);
            match.SetNpcMode(NpcAiMode.Idle); match.SetRenderInterpolation(false);
            var renderers = FindObjectsByType<PlayerRenderer>(FindObjectsSortMode.None).Where(r => r.CharacterDef != null).OrderBy(r => r.EntityId).ToArray();
            var mine = renderers.First(r => r.EntityId == 1);
            var other = renderers.First(r => r.EntityId != 1);
            var entry = ClientSession.MatchContentCatalog.ResolvePackage(fighter);
            match.CaptureSimulationTickCompleted += t =>
            {
                var observed = match.GetCaptureState(1);
                if (observed.State == ActionState.Attacking)
                    trace.AppendLine(string.Join(",",fighter,form,"apply",Time.frameCount,t,observed.AttackElapsedTicks,
                        mine.GetComponentInChildren<AnimancerComponent>().States.Current.Time.ToString(CultureInfo.InvariantCulture)));
            };
            BodyClockScene.Freeze(match, 1.5f);
            match.enabled = true; mine.enabled = true; other.enabled = true; Time.timeScale = 1;
            Time.captureFramerate = output.EndsWith("30") ? 30 : 60;
            foreach (string phase in new[] { "idle", "run", "hitstun", "attack" })
            {
                var s = match.GetCaptureState(1);
                s.State = ActionState.Idle; s.HitstunTicks = 0; s.HitstopTicks = 0; s.AttackSlot = 0; s.AnimLockTicks = 0;
                s.PX = -2; s.PZ = 0; s.PY = match.CaptureSimulation.GetDefinition(1).CapsuleHeight * .5f; s.VX = s.VY = s.VZ = s.KVX = s.KVY = s.KVZ = 0; s.IsGrounded = true;
                if (phase == "hitstun") { s.State = ActionState.Hitstun; s.HitstunTicks = 90; s.HitstunLevel = 2; s.HitReactionSequence++; }
                match.SetCaptureState(1, s);
                for (int tick = 0; tick < 120; tick++)
                {
                    match.SetCaptureInput(new InputState { MoveX = phase == "run" && tick < 100 ? .5f : 0, ActiveSlot = (byte)(phase == "attack" && tick == 5 ? AbilitySlots.Normal1 : 0) });
                    yield return new WaitForEndOfFrame();
                    Measure(match, mine, other, entry, fighter, form, phase, tick);
                    if (fighter == "fightguy" && (phase == "run" || phase == "hitstun"))
                    {
                        mine.transform.SetPositionAndRotation(new Vector3(-1.3f,1.5f + mine.ModelYOffset,0),Quaternion.Euler(0,0,0));
                        other.transform.SetPositionAndRotation(new Vector3(1.3f,1.5f + other.ModelYOffset,0),Quaternion.Euler(0,0,0));
                        var camera = Camera.main;
                        camera.Render();
                        ScreenCapture.CaptureScreenshot(Path.Combine(output,$"{phase}-{tick:000}.png"));
                        match.enabled = true; mine.enabled = true; other.enabled = true; Time.timeScale = 1;
                    }
                }
            }
            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(output,"measurements.csv"),csv.ToString());
            File.WriteAllText(Path.Combine(output,"trace.csv"),trace.ToString());
        }
    }
    void Measure(TrainingMatch match, PlayerRenderer mine, PlayerRenderer other, MatchContentEntry entry, string fighter, byte form, string phase, int tick)
    {
        var s = match.GetCaptureState(1);
        if (s.State == ActionState.Attacking)
            trace.AppendLine(string.Join(",",fighter,form,"draw",Time.frameCount,match.CaptureTick,s.AttackElapsedTicks,
                mine.GetComponentInChildren<AnimancerComponent>().States.Current.Time.ToString(CultureInfo.InvariantCulture)));
        var sim = match.CaptureSimulation;
        var args = new object[] { 1UL, s, sim.GetDefinition(1), null, null, 0f, false };
        if (!(bool)Resolve.Invoke(sim,args)) return;
        var baked = (BakedAnimationData)args[3]; string auth = (string)args[4]; float af = (float)args[5];
        int ai = baked.FindAnimIndex(auth);
        if (s.State != ActionState.Attacking && s.State != ActionState.Aiming) af = ((int)af - 1 + baked.Animations[ai].FrameCount) % baked.Animations[ai].FrameCount;
        var ac = mine.GetComponentInChildren<AnimancerComponent>(); var current = ac.States.Current;
        if (current?.Clip == null) return;
        var binding = mine.PresentationCatalog.Animations.FirstOrDefault(a => a.Clip == current.Clip);
        if (binding == null) return;
        float rf = (float)(current.Time * 60);
        rf = current.IsLooping ? rf % binding.FrameCount : Math.Min(rf,binding.FrameCount-1);
        var drawn = Points.Select(p => Distance(mine,baked,ai,af,p)).ToArray();
        var proposed = mine.PresentationCatalog.Animations.FirstOrDefault(a => a.PoseTrackId == auth);
        var oc = other.GetComponentInChildren<AnimancerComponent>();
        var right = oc.Play(proposed.Clip,0); right.Time = af / 60f; right.Speed = 0; oc.Evaluate(0);
        var corrected = Points.Select(p => Distance(other,baked,ai,af,p)).ToArray();
        for (int i = 0; i < Points.Length; i++)
            csv.AppendLine(string.Join(",",fighter,form,phase,tick,s.State,binding.SemanticId,auth,rf.ToString(CultureInfo.InvariantCulture),af.ToString(CultureInfo.InvariantCulture),current.Weight.ToString(CultureInfo.InvariantCulture),Points[i],drawn[i].ToString(CultureInfo.InvariantCulture),corrected[i].ToString(CultureInfo.InvariantCulture)));
    }
    static float Distance(PlayerRenderer r, BakedAnimationData baked, int anim, float frame, string point)
    {
        int index = baked.FindBoneIndex(point), hips = baked.HipsOffsetIndex;
        var bone = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == point);
        if (index < 0 || hips < 0 || bone == null) return float.NaN;
        int f0 = Math.Max(0,Math.Min((int)frame,baked.Animations[anim].FrameCount-1)); int f1 = Math.Min(f0+1,baked.Animations[anim].FrameCount-1);
        Vector3 At(int f) { baked.TryGetPoint(anim,f,hips,out float hx,out float hy,out float hz); baked.TryGetPoint(anim,f,index,out float x,out float y,out float z); return new Vector3(hx+x,hy+y,hz+z); }
        var root = r.Animator.transform;
        Vector3 drawn = Vector3.Scale(root.localScale,root.InverseTransformPoint(bone.position)) + Vector3.up*r.ModelYOffset;
        return Vector3.Distance(Vector3.Lerp(At(f0),At(f1),frame-f0),drawn)*1000;
    }
}

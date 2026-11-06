#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SlopArena.Client.UI;
using SlopArena.Shared;
using SlopArena.Shared.Mayhem;
using SlopArena.Shared.AI;
using UnityEngine;

namespace SlopArena.Client.World
{
    public sealed class MankiMayhemCapture : MonoBehaviour
    {
        public TrainingMatch Match;
        public string Output;
        public bool FullShow;
        public MankiHostPresenter Host;
        public bool Done { get; private set; }
        private readonly Dictionary<ulong, (byte Hits, uint Tick)> _boardChanges = new();
        private readonly HashSet<int> _carried = new();
        private int _carryEpisode = -1;
        private readonly HashSet<string> _shots = new();
        private readonly Queue<string> _pending = new();
        private readonly List<object> _evidence = new();
        private readonly object _inputOwner = new();
        private readonly MayhemCpu _cpu = new(CpuDifficulty.Normal);
        private int _rate, _endFrames;
        private float _fixed;
        private void OnEnable()
        {
            _rate=Time.captureFramerate; _fixed=Time.fixedDeltaTime;
            Time.captureFramerate=60; Time.fixedDeltaTime=1f/60f;
            if (!Match.TryAcquireCaptureInputSequence(_inputOwner)) throw new InvalidOperationException("Capture input owned.");
            Match.CaptureSimulationTickCompleted += Tick;
            StartCoroutine(Capture());
        }
        private void Shot(string name) { if (_shots.Add(name)) _pending.Enqueue(name); }
        private void Tick(uint tick)
        {
            var sim=Match.CaptureSimulation; var v=sim.MayhemView; if(v==null) return;
            var c=v.Cursor; string channel=v.Channel?.CopyKey.Replace("mayhem.channel.","")??"show";
            string twist = v.Airing is RemixAiring remix
                ? "prime-time." + remix.PairCopyKey.Replace("mayhem.pair.", "") + "."
                    + remix.GoalTwistCopyKey.Replace("mayhem.twist.", "") + "."
                    + remix.HazardTwistCopyKey.Replace("mayhem.twist.", "")
                : v.Airing is SoloAiring solo ? solo.TwistCopyKey.Replace("mayhem.twist.", "") : channel;
            uint age=tick-c.PhaseStart;
            if(c.Phase==MayhemPhase.Call && age>=15) Shot(twist+"-call");
            if(c.Phase==MayhemPhase.Play && channel=="no-running" && WatchSafe(v)) Shot("watch-safe");
            if (c.Phase == MayhemPhase.Play)
            {
                bool readable = v.Airing is RemixAiring airing
                    ? airing.Goal.Channel == MayhemChannels.BreakOut
                        ? v.Scene.Bombs.Any(b => b.Lane == 1 && b.Launched && !b.Exploded && tick >= b.LaunchTick + 15)
                        : v.Scene.Zones.Any(z => z.ActiveAt(tick))
                            && v.Scene.Rings.Any(r => tick >= r.SlamTick + 15 && tick < r.SlamTick + r.LifetimeTicks)
                    : channel == "get-him" ? v.Scene.Targets.Any(t => t.Kind == MayhemTargetKind.DizzyHost && t.ActiveAt(tick) && tick >= t.ActiveFrom + 24)
                    : channel == "quick-draw" ? MankiMayhemView.DrawWord(v) == "DRAW!"
                    : channel == "hot-potato" ? v.Scene.Carries.Count > 0 && age >= 24
                    : channel == "block" ? v.Scene.Bombs.Any(b => b.Kind == MayhemBombKind.Barrage && tick >= b.LaunchTick && tick < b.ImpactTick)
                    : channel == "bonk" ? v.Scene.Targets.Any(t => t.Kind != MayhemTargetKind.BoardStack && t.ActiveAt(tick) && tick >= t.ActiveFrom + 10)
                    : channel == "glitch" ? v.Scene.Zones.Any(z => z.Kind == MayhemZoneKind.GlitchPanel && z.ActiveAt(tick) && !z.ArmedAt(tick) && tick >= z.ActiveFrom + 15)
                    : channel == "smile" ? v.Scene.Zones.Any(z => z.Kind == MayhemZoneKind.SelfieFrame && tick + 30 >= z.ArmedFrom && tick < z.ArmedFrom)
                    : channel == "sumo" ? tick >= c.PlayStart + (c.Deadline - c.PlayStart) * 2 / 3
                    : channel == "dodge" ? v.Scene.Bombs.Any(b => b.Launched && !b.Exploded && tick >= b.LaunchTick + 15)
                    : channel == "jump" ? v.Scene.Rings.Any(r => tick >= r.SlamTick + 15 && tick < r.SlamTick + 30)
                    : channel == "break-out" || channel == "break-boards" ? age >= 15
                    : twist == "spot.musical" ? v.Scene.Zones.Any(z => z.ArmedAt(tick))
                    : twist == "no-running.fake-out" ? MankiMayhemView.FakeTurn(v) > .7f
                    : tick >= c.PlayStart + (c.Deadline - c.PlayStart) / 2;
                if (readable) Shot(twist + "-play");
            }
            if(c.Phase==MayhemPhase.Verdict && age>=10) Shot(twist+"-verdict");
            if(c.Phase==MayhemPhase.Verdict && channel=="spot" && age>=1) Shot("spot-buzzer");
            if(c.Phase==MayhemPhase.SpeedUp && age>=15) Shot("speed-up-act-"+(c.Act+1));
            if(v.Contestants.Any(p=>p.Failure!=null)) Shot("splat");
            foreach(var e in sim.LastTickMayhemEvents)
            {
                if(e.Kind==MayhemEventKind.WatchWarning) Shot("watch-warning");
                if(e.Kind==MayhemEventKind.WatchStart) Shot("watching");
                if(e.Kind==MayhemEventKind.Launch) Shot("host-throw");
                if(e.Kind==MayhemEventKind.Slam) Shot("host-slam");
                if(e.Kind==MayhemEventKind.TargetHit) Shot("boards-cracked");
                if(e.Kind==MayhemEventKind.Fail) Shot("splat-"+e.SplatStyle.ToString().ToLowerInvariant());
            }
            if (c.Phase == MayhemPhase.Play)
            {
                foreach (var watch in v.Scene.Watches)
                    if ((watch.Kind == MayhemWatchKind.Draw || watch.Kind == MayhemWatchKind.FakeDraw)
                        && tick >= watch.StartTick && tick < watch.EndTick)
                        Shot(twist + "-" + watch.CopyKey.Replace("mayhem.draw.", "word-"));
                if (_carryEpisode != c.Episode) { _carried.Clear(); _carryEpisode = c.Episode; }
                var currentCarries = new HashSet<int>(v.Scene.Carries.Select(p => p.Lane * 65536 + p.Ordinal));
                foreach (var old in _carried)
                    if (!currentCarries.Contains(old)) Shot(twist + "-retired-" + old);
                _carried.Clear(); foreach (var key in currentCarries) _carried.Add(key);
                foreach (var carry in v.Scene.Carries)
                {
                    Shot(twist + "-holder-" + carry.Ordinal + "-seat-" + carry.HolderSeat);
                    if (carry.LastGiver != null) Shot(twist + "-return-lock");
                }
                foreach (var progress in v.Scene.HitProgress)
                    if (progress.Hits > 0) Shot(twist + "-quota-" + progress.Hits);
                foreach (var failure in v.Contestants)
                    if (failure.Failure != null) Shot(twist + "-" + MankiMayhemView.FailureText(failure.Failure.Value).Replace(" ", "-").ToLowerInvariant());
                foreach (var solid in v.Scene.Solids)
                {
                    if (solid.HitsLeft == solid.HitBudget) Shot("props-cage-intact");
                    if (solid.HitsLeft == solid.HitBudget - 1) Shot($"cage-{solid.CageEntityId}-first-hit");
                    if (solid.HitsLeft == 0) { Shot($"cage-{solid.CageEntityId}-open"); Shot("props-cage-broken"); }
                    else if (solid.HitsLeft > 0) Shot("props-cage-tear");
                    else Shot("props-cage-broken");
                }
                foreach (var target in v.Scene.Targets)
                {
                    if (target.Kind != MayhemTargetKind.BoardStack) continue;
                    if (!_boardChanges.TryGetValue(target.EntityId, out var change) || change.Hits != target.HitsLeft)
                    { change = (target.HitsLeft, tick); _boardChanges[target.EntityId] = change; }
                    if (target.HitsLeft == target.Hits) Shot("props-boards-intact");
                    else if (tick - change.Tick >= 12)
                    {
                        if (target.HitsLeft == 0) Shot("props-boards-all-split");
                        else if (target.HitsLeft == target.Hits - 1) Shot("props-boards-one-split");
                    }
                }
                foreach (var zone in v.Scene.Zones)
                {
                    if (zone.Kind == MayhemZoneKind.SelfieFrame && tick == zone.ArmedFrom) Shot(twist + "-flash");
                    if (zone.Kind == MayhemZoneKind.GlitchPanel && zone.ArmedAt(tick)) Shot(twist + "-missing-panels");
                }
                foreach (var target in v.Scene.Targets)
                    if (target.Kind != MayhemTargetKind.BoardStack && target.HitsLeft == 0) Shot(twist + "-claimed-head");
                foreach (var ring in v.Scene.Rings)
                {
                    if (channel == "get-him" && tick >= ring.SlamTick && tick < ring.SlamTick + 12) Shot(twist + "-wake");
                    if ((long)tick - ring.SlamTick == ring.LifetimeTicks - 3) Shot($"wave-widest-act-{c.Act + 1}");
                    if ((long)tick - ring.SlamTick == 100) Shot($"wave-deck-edge-act-{c.Act + 1}");
                    if ((long)tick - ring.SlamTick >= 25 && (long)tick - ring.SlamTick < 28)
                        Shot($"props-wave-act-{c.Act + 1}-slam-{ring.Ordinal}");
                }
                foreach (var bomb in v.Scene.Bombs)
                {
                    if (bomb.Kind == MayhemBombKind.Barrage && tick >= bomb.LaunchTick && tick < bomb.ImpactTick) Shot(twist + "-warning-" + bomb.Ordinal);
                    if (bomb.Kind == MayhemBombKind.Barrage && tick >= bomb.ImpactTick && tick < bomb.ImpactTick + 12) Shot(twist + "-blast-" + bomb.Ordinal);
                    if (channel == "get-him" && bomb.Launched) Shot(twist + "-wake");
                    if (channel == "dodge" && bomb.Launched && tick <= bomb.LaunchTick + 2) Shot("bomb-just-launched");
                    if (channel == "dodge" && bomb.Launched && !bomb.Exploded && tick > bomb.LaunchTick + (bomb.ImpactTick - bomb.LaunchTick) * .65f) Shot("props-bomb-falling");
                    if (channel == "dodge" && bomb.Exploded) Shot("props-bomb-blast");
                }
                if (v.Scene.Zones.Count > 0 && v.Contestants.Any(p => p.EntityId == MatchConfig.LocalEntityId && p.ObjectiveMet)) Shot("spot-p1-holding");
                if (v.Scene.Zones.Count > 0 && v.Contestants.Any(p => p.ObjectiveMet)) Shot("props-spot-holder");
            }
            if(c.Phase==MayhemPhase.Intro) Shot("intro");
            Match.SetCaptureSequenceInput(_inputOwner,_cpu.Decide(sim,MatchConfig.LocalEntityId));
        }
        private IEnumerator Capture()
        {
            var end=new WaitForEndOfFrame();
            while(!Done)
            {
                yield return end;
                var sim=Match.CaptureSimulation; var v=sim.MayhemView; if(v==null) continue;
                if(v.Cursor.Phase==MayhemPhase.Ended && ++_endFrames==30) Shot("show-ended");
                if(_pending.Count>0)
                {
                    var pixels=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
                    pixels.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false);
                    while(_pending.Count>0)
                    {
                        string name=_pending.Dequeue(); File.WriteAllBytes(Path.Combine(Output,name+".png"),pixels.EncodeToPNG());
                        var mount = FindFirstObjectByType<SlopArena.Client.Camera.CameraMount>();
                        var camera = mount.RenderCamera;
                        _evidence.Add(new { shot=name, view=Newtonsoft.Json.Linq.JToken.FromObject(v), width=Screen.width,height=Screen.height,host=Host?.Reaction,
                            option=MankiMayhemPresentation.SelectedCamera.ToString(),
                            reducedFlashing=ClientSettingsService.Instance.ReducedFlashing,
                            camera=new { pitch=mount.GetCameraPitchDeg(), radius=mount.GetOrbitRadius(), camera.fieldOfView,
                                position=new[] { camera.transform.position.x,camera.transform.position.y,camera.transform.position.z } },
                            rings=v.Scene.Bombs.Select(b => new { b.Lane,b.Ordinal,kind="bomb", inFrame=DiscInFrame(camera,new Vector3(b.Landing.X,b.Landing.Y,b.Landing.Z),b.BlastRadius) }).ToArray(),
                            spotlights=v.Scene.Zones.Select(z => new { z.Lane,z.Ordinal,inFrame=DiscInFrame(camera,new Vector3(z.Center.X,z.Center.Y,z.Center.Z),z.Radius) }).ToArray(),
                            fighters=sim.GetAllStates().Select(p => {
                                float height=sim.GetDefinition(p.Key).CapsuleHeight;
                                var center=new Vector3(p.Value.PX,p.Value.PY,p.Value.PZ);
                                var top=camera.WorldToViewportPoint(center+Vector3.up*height*.5f);
                                var bottom=camera.WorldToViewportPoint(center-Vector3.up*height*.5f);
                                return new { id=p.Key, capsuleHeight=height, pixels=Mathf.Abs(top.y-bottom.y)*camera.pixelHeight,
                                    top=new[] { top.x,top.y,top.z }, bottom=new[] { bottom.x,bottom.y,bottom.z },
                                    inFrame=top.z>0 && bottom.z>0 && top.x>=0 && top.x<=1 && bottom.x>=0 && bottom.x<=1 && top.y<=1 && bottom.y>=0 };
                            }).ToArray() });
                    }
                    Destroy(pixels);
                }
                if(_endFrames>=60)
                {
                    Done=true;
                    File.WriteAllText(Path.Combine(Output,"scenario.json"),JsonConvert.SerializeObject(new { shots=_shots, frames=_evidence, tick=sim.CurrentTick,view=v },Formatting.Indented));
                }
            }
        }
        private static bool WatchSafe(MayhemShowView view)
        {
            foreach (var cue in view.Scene.Watches)
                if (view.Tick >= cue.WarningTick && view.Tick < cue.EndTick) return false;
            return true;
        }
        private static bool DiscInFrame(UnityEngine.Camera camera, Vector3 center, float radius)
        {
            for (int i=0;i<32;i++)
            {
                float angle=i*Mathf.PI/16;
                var p=camera.WorldToViewportPoint(center+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius));
                if (p.z<=0 || p.x<0 || p.x>1 || p.y<0 || p.y>1) return false;
            }
            return true;
        }

        private void OnDisable()
        {
            Match.CaptureSimulationTickCompleted-=Tick; Match.ReleaseCaptureInputSequence(_inputOwner);
            Time.captureFramerate=_rate; Time.fixedDeltaTime=_fixed;
        }
    }
}
#endif

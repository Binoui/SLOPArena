using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SlopArena.Client;
using SlopArena.Client.Combat;
using SlopArena.Client.Replays;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Shared.Replays;
using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace SlopArena.EditorTools
{
    /// <summary>Gateway commands for the in-game Replay viewer: fixtures, opening a file at a tick, and checks.</summary>
    public static class ReplayViewerCommands
    {
        [CliCommand("sloparena.replays.build-scene", "Regenerate Scenes/Replay.unity from Arena_Offline and list it in the build settings.",
            MainThreadRequired = true, Tags = new[] { "replays" })]
        public static object BuildScene() => new { success = true, scene = ReplaySceneBuilder.Build() };

        [CliCommand("sloparena.replays.record-bots", "Record a same-build CPU Match (stock or an online mode) as a .slopreplay with this Editor's content, so it plays Exact here.",
            MainThreadRequired = true, Tags = new[] { "replays", "testing" })]
        public static object RecordBots(
            [CliArg("output", "Repository-relative or absolute output path ending in .slopreplay.", Required = true)] string output,
            [CliArg("fighters", "Comma-separated package IDs, 2 to 4; append :alternate to start in the alternate form.")] string fighters = "fightguy,nyx:alternate",
            [CliArg("arena", "Baked arena name; empty picks the format's usual arena.")] string arena = "",
            [CliArg("ticks", "Recorded ticks (60 per second).")] int ticks = 1800,
            [CliArg("seed", "Bot random seed.")] int seed = 7,
            [CliArg("match-format", "stock, slopball-coop, slopball-versus, course-race, course-coop, course-scramble or manki-mayhem.")] string format = "stock")
        {
            if (!output.EndsWith(".slopreplay", StringComparison.Ordinal)) throw new ArgumentException("output must end with .slopreplay");
            if (!MatchFormats.TryParseWire(format, out var matchFormat)) throw new ArgumentException($"Unknown format '{format}'.");
            if (arena.Length == 0)
                arena = matchFormat switch
                {
                    MatchFormat.Stock => "slop_pit",
                    MatchFormat.SlopballCoop or MatchFormat.SlopballVersus => "slopball",
                    MatchFormat.MankiMayhem => "manki_mayhem",
                    _ => "fightguy_pier",
                };
            string path = Path.GetFullPath(Path.IsPathRooted(output) ? output : Path.Combine(RepositoryRoot, output));
            if (!ClientSession.TryBuildLocalMatchCatalog(out var catalog, out var failure) || catalog == null)
                throw new InvalidOperationException(failure);
            string arenaPath = BakedContentPaths.ResolveArena(arena) ?? throw new ArgumentException($"No baked arena '{arena}'.");
            if (!MatchArenaRecord.TryLoad(arenaPath, arena, out var identity, out var definition, out var arenaFailure) || identity == null)
                throw new InvalidOperationException(arenaFailure);
            var picks = fighters.Split(',').Select(f => f.Trim().Split(':')).ToArray();
            if (picks.Length is < 2 or > 4) throw new ArgumentException("fighters must name 2 to 4 packages.");
            var entries = picks.Select(p => catalog.ResolvePackage(p[0]) ?? throw new ArgumentException($"No package '{p[0]}'.")).ToArray();
            if (!matchFormat.Admits(definition) && !(matchFormat == MatchFormat.CourseScramble && definition.Arcade?.Course != null))
                throw new ArgumentException($"Arena '{arena}' cannot host {format}.");
            var camps = entries.Select((_, i) => matchFormat == MatchFormat.SlopballVersus ? (i % 2 == 0 ? SlopballCamp.Teal : SlopballCamp.Clay) : SlopballCamp.None).ToArray();
            var roster = entries.Select((e, i) => new ReplayParticipant((ulong)i + 1, "cpu", i.ToString(CultureInfo.InvariantCulture), e.Identity,
                picks[i].Length > 1 && picks[i][1] == "alternate" ? "alternate" : "base", camps[i].ToWire(), i.ToString(CultureInfo.InvariantCulture))).ToArray();
            // The GameHost's content hash: the whole roster handle map with the Match arena (MultiMatchOrchestrator).
            var records = catalog.Entries.Where(e => e.LegacySelector.HasValue)
                .Select(e => new MatchContentHandleRecord(e.Handle, e.LegacySelector!.Value, e.Identity, e.DisplayName)).ToList();
            string hash = SteamMatchDescriptor.HashContent(new MatchContentHandleMap(MatchContentHandleMap.CurrentSchemaVersion, records, identity));
            var header = new ReplayHeader("editor-bots-" + seed, arena, new ReplayRules(format, "off", 4, false), 1, roster,
                ReplayBuildIdentity.Current("editor", hash), identity.Digest);
            var sim = new ServerSimulation(definition, matchFormat.CreateRule(4, FriendlyFireMode.Off,
                roster.ToDictionary(p => p.EntityId, p => camps[(int)p.EntityId - 1])));
            for (int i = 0; i < entries.Length; i++)
            {
                // The GameHost's starts (MatchInstance.PickSpawn): stock uses spawn points, modes their own layout.
                var spawn = matchFormat == MatchFormat.Stock ? definition.SpawnPoints[i % definition.SpawnPoints.Length]
                    : matchFormat.StartFor(definition, i, entries.Length, camps);
                var state = new CharacterState
                {
                    EntityId = (ulong)i + 1, PX = spawn.X, PY = spawn.Y, PZ = spawn.Z, FacingYaw = spawn.Yaw, IsGrounded = true,
                    JumpsLeft = entries[i].Definition.Movement.MaxJumps, AirDodgesLeft = 1,
                };
                state.Form.Active = (byte)(roster[i].FormId == "alternate" ? 1 : 0);
                sim.RegisterEntity(state.EntityId, entries[i].Definition, state, entries[i].BakedAnimation);
            }
            if (matchFormat == MatchFormat.SlopballCoop) sim.BeginSlopballRun((uint)seed);
            if (matchFormat == MatchFormat.SlopballVersus) sim.BeginVersusSlopball();
            if (matchFormat.IsCourse()) sim.BeginOnlineCourse();
            if (matchFormat == MatchFormat.MankiMayhem) sim.BeginMankiMayhem((uint)seed);
            var capture = new ReplayCapture(header, catalog, definition);
            var policy = new HeuristicBotPolicy();
            var rng = new System.Random(seed);
            var memories = entries.Select(_ => new BotMemory { Difficulty = CpuDifficulty.Normal }).ToArray();
            var inputs = new Dictionary<ulong, InputState>();
            int perching = 0, hits = 0, modeEvents = 0;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path))
            using (var writer = new ReplayWriter(stream, header))
            {
                for (int tick = 0; tick < ticks; tick++)
                {
                    for (int i = 0; i < entries.Length; i++)
                    {
                        ulong id = (ulong)i + 1;
                        for (int j = 0; j < entries.Length; j++)
                            if (j != i) policy.ObserveOpponent(memories[i], sim.GetState((ulong)j + 1), sim.GetDefinition((ulong)j + 1), entries[j].BakedAnimation, sim.GetActiveMove((ulong)j + 1)?.Slot);
                        memories[i].ChooseOpponent(sim.GetState(id));
                        inputs[id] = policy.Decide(sim.GetState(id), sim.GetDefinition(id), rng, memories[i], definition, entries[i].BakedAnimation, activeMove: sim.GetActiveMove(id)?.Slot,
                            mayhemThreats: matchFormat == MatchFormat.MankiMayhem ? new MayhemThreats(sim) : null);
                    }
                    sim.Tick(inputs);
                    var frame = new WorldFramePacket(MatchState.Playing, sim.CaptureSnapshot(), Array.Empty<RelayedInput>(),
                        sim.GetPresentationEvents(true).Where(e => WorldFramePacket.CanCarry(e)).ToArray(), sim.LastTickHits.ToArray(),
                        sim.LastTickSlopballEvents.Select(e => new SlopballTickEvent(sim.CurrentTick, e)).ToArray(),
                        sim.LastTickOnlineCourseEvents.ToArray(), sim.LastTickMayhemEvents.ToArray());
                    var captured = capture.Capture(sim, frame);
                    if (captured.Status != ReplayCaptureStatus.Accepted) throw new InvalidOperationException(captured.Detail);
                    if (writer.Append(captured.Capture!) != ReplayAppend.BufferedOrCommitted) throw new InvalidOperationException("The Replay writer stopped.");
                    perching += captured.Capture!.View.Shapes.Count(s => s.VisualKindId == "perching");
                    hits += captured.Capture.View.Hits.Count;
                    modeEvents += captured.Capture.View.Events.Count(e => e is not ReplayPresentationEvent);
                }
                writer.Complete(new ReplayConclusion(ReplayEndReason.Cancelled, "editor bot fixture", null));
            }
            return new { success = true, path, format, arena, ticks, fighters = roster.Select(r => r.Package.PackageId + ":" + r.FormId).ToArray(), perchingShapeTicks = perching, hits, modeEvents };
        }

        [CliCommand("sloparena.replays.directory-selftest", "Check the Replay folder rule against tools/tests/replay_directory_vectors.json, the vectors replay_pull.py also uses.",
            MainThreadRequired = true, Tags = new[] { "replays", "testing" })]
        public static object DirectorySelfTest()
        {
            var vectors = JArray.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "tools/tests/replay_directory_vectors.json")));
            var results = new List<object>();
            bool success = true;
            foreach (var vector in vectors)
            {
                var host = (string)vector["host"]! switch
                {
                    "linux" => ReplayHostKind.Linux,
                    "windows" => ReplayHostKind.Windows,
                    "wine" => ReplayHostKind.WindowsUnderWine,
                    "macos" => ReplayHostKind.MacOS,
                    var other => throw new InvalidDataException("host " + other),
                };
                var env = ((JObject)vector["env"]!).Properties().ToDictionary(p => p.Name, p => (string?)p.Value);
                string actual = ReplayDirectory.Resolve(new ReplayDirectoryProbe(host, name => env.TryGetValue(name, out var v) ? v : null));
                bool pass = actual == (string)vector["root"]!;
                success &= pass;
                results.Add(new { name = (string)vector["name"]!, pass, actual });
            }
            return new { success, vectors = results, current = ReplayDirectory.Resolve(ReplayDirectoryProbe.Current()) };
        }

        [CliCommand("sloparena.replays.list-check", "Write complete, partial, newer-build and garbage files to a folder, point the Replay folder at it in this Editor process, and check the list's rows.",
            MainThreadRequired = true, Tags = new[] { "replays", "testing" })]
        public static async Task<object> ListCheck(
            [CliArg("directory", "Repository-relative folder to (re)create for the check.", Required = true)] string directory,
            [CliArg("source", "A complete .slopreplay to derive the files from.", Required = true)] string source)
        {
            string root = Path.GetFullPath(Path.Combine(RepositoryRoot, directory));
            string from = Path.GetFullPath(Path.IsPathRooted(source) ? source : Path.Combine(RepositoryRoot, source));
            if (!root.StartsWith(Path.Combine(RepositoryRoot, "tmp") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("directory must be below tmp/.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(Path.Combine(root, "2026-11-02"));
            var bytes = File.ReadAllBytes(from);
            File.WriteAllBytes(Path.Combine(root, "2026-11-02", "complete.slopreplay"), bytes);
            File.WriteAllBytes(Path.Combine(root, "2026-11-02", "partial.slopreplay.part"), bytes.Take(bytes.Length * 2 / 3).ToArray());
            var newer = (byte[])bytes.Clone();
            newer[8] = 2;
            File.WriteAllBytes(Path.Combine(root, "newer.slopreplay"), newer);
            var garbage = new byte[4096];
            new System.Random(5).NextBytes(garbage);
            File.WriteAllBytes(Path.Combine(root, "garbage.slopreplay"), garbage);
            Environment.SetEnvironmentVariable(ReplayDirectory.OverrideVariable, root);
            string resolved = ReplayDirectory.Resolve(ReplayDirectoryProbe.Current());
            var vanished = new FileInfo(Path.Combine(root, "vanished.slopreplay.part"));
            File.WriteAllBytes(vanished.FullName, bytes);
            File.Delete(vanished.FullName);
            var failedRow = (ReplayListing)typeof(ReplayLibrary).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { vanished });
            if (failedRow.Failure?.Code != ReplayFailureCode.IoFailure || failedRow.CanOpen)
                throw new InvalidOperationException("A deleted enumerated file must become an IO failure row.");
            var scanFailure = ReplayLibrary.Scan(Path.Combine(root, "garbage.slopreplay"), System.Threading.CancellationToken.None);
            if (scanFailure.Count != 1 || scanFailure[0].Failure?.Code != ReplayFailureCode.IoFailure)
                throw new InvalidOperationException("A scan-level IO failure must become a row.");
            var rows = await ReplayLibrary.ScanAsync(resolved, System.Threading.CancellationToken.None);
            var overlay = new ReplayListOverlay(new UnityEngine.UIElements.VisualElement());
            overlay.Close();
            var labels = rows.ToDictionary(r => Path.GetFileName(r.Path), r => overlay.Detail(r));
            bool Has(string file, params string[] parts) => labels.TryGetValue(file, out var label) && parts.All(label.Contains);
            bool success = resolved == root && rows.Count == 4
                && Has("complete.slopreplay", "Downloaded", "Complete")
                && Has("partial.slopreplay.part", "Downloaded", "Partial")
                && Has("newer.slopreplay", "Made by a newer build. Update SLOPArena.")
                && Has("garbage.slopreplay", "Not a readable Replay file.")
                && rows.Single(r => r.Path.EndsWith("complete.slopreplay", StringComparison.Ordinal)).CanOpen
                && !rows.Single(r => r.Path.EndsWith("garbage.slopreplay", StringComparison.Ordinal)).CanOpen;
            return new { success, root = resolved, homeShowsReplays = ReplayDirectory.HasAnyReplay(resolved), rows = labels };
        }

        [CliCommand("sloparena.replays.open", "Open a .slopreplay in the Replay viewer (Play Mode), optionally at a tick, focused fighter and track.",
            MainThreadRequired = true, Tags = new[] { "replays" })]
        public static async Task<object> Open(
            [CliArg("file", "Path to a .slopreplay or .slopreplay.part file.", Required = true)] string file,
            [CliArg("tick", "Absolute tick to open at; the first recorded tick when omitted.")] string tick = null,
            [CliArg("focus", "Entity ID to follow; the first fighter when omitted.")] string focus = null,
            [CliArg("track", "best or view.")] string track = "best")
        {
            RequirePlayMode();
            string path = Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(RepositoryRoot, file));
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            var choice = track switch
            {
                "best" => ReplayTrackChoice.Best,
                "view" => ReplayTrackChoice.ViewOnly,
                _ => throw new ArgumentException("track must be best or view."),
            };
            var previous = MatchBase.Active;
            ReplayLaunch.Open(path, FrontendPage.Home, choice,
                tick == null ? null : uint.Parse(tick, CultureInfo.InvariantCulture),
                focus == null ? null : ulong.Parse(focus, CultureInfo.InvariantCulture));
            var match = await WaitFor(() => ActiveReplay() is { PresentedTicks: > 0 } m && m != previous ? m : null, 20,
                () => ReplayLaunch.LastFailure ?? "The Replay viewer did not present a tick.");
            return Describe(match);
        }

        [CliCommand("sloparena.replays.check", "Check the open Replay viewer. play: every tick at 2x, ten step-backs and step-forwards, every fighter focused, no console errors.",
            MainThreadRequired = true, Tags = new[] { "replays", "testing" })]
        public static async Task<object> Check(
            [CliArg("script", "play; pose: a sword move's weapon tip follows the recorded pose clock; orbit: the camera orbits and zooms while paused; seek-hygiene: what Seek(B) shows does not depend on what came before.", Required = true)] string script,
            [CliArg("screenshot-tick", "Tick offset from the first tick to leave the viewer paused on afterwards.")] int screenshotTick = 120)
        {
            RequirePlayMode();
            var match = ActiveReplay() ?? throw new InvalidOperationException("Open a Replay first (sloparena.replays.open).");
            if (script == "resume") return await ResumeCheck(match);
            if (script == "scrub-clock") return ScrubClockCheck(match);
            if (script == "pose") return PoseCheck(match);
            if (script == "orbit") return await OrbitCheck(match);
            if (script == "seek-hygiene") return SeekHygiene(match);
            if (script != "play") throw new ArgumentException("script must be play, pose, orbit, seek-hygiene, resume or scrub-clock.");
            var errors = new List<string>();
            void Log(string message, string stack, LogType type)
            {
                if (type is LogType.Error or LogType.Exception or LogType.Assert) errors.Add(message);
            }
            Application.logMessageReceived += Log;
            try
            {
                var replay = match.Replay!;
                var range = replay.Range;
                var perchTicks = new List<uint>();
                var perchShown = new List<uint>();
                var projectiles = match.GetComponent<ProjectileVFXManager>();
                if (!match.Seek(range.First)) throw new InvalidOperationException("Seek to the first tick failed.");
                int start = match.PresentedTicks;
                match.SetSpeed(ReplaySpeed.Two);
                if (match.Transport.State != ReplayTransportState.Playing) match.TogglePlay();
                double timeout = (range.Last - range.First) / 120.0 + 30.0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int firstFrame = Time.frameCount;
                uint lastSeen = range.First;
                while (match.Transport.State != ReplayTransportState.Ended)
                {
                    if (watch.Elapsed.TotalSeconds > timeout) throw new TimeoutException($"Playback stopped at {replay.CurrentTick} of {range.Last}.");
                    await Task.Delay(1);
                    if (replay.CurrentTick is { } now && now != lastSeen)
                    {
                        lastSeen = now;
                        if (ShowsPerching(replay.World, projectiles, out bool shown))
                        {
                            perchTicks.Add(now);
                            if (shown) perchShown.Add(now);
                        }
                    }
                }
                watch.Stop();
                int frames = Time.frameCount - firstFrame;
                int played = match.PresentedTicks - start;
                uint end = replay.CurrentTick!.Value;
                for (int i = 0; i < 10; i++) match.StepBack();
                uint back = replay.CurrentTick!.Value;
                for (int i = 0; i < 10; i++) match.StepForward();
                uint forward = replay.CurrentTick!.Value;
                var focused = new List<ulong> { match.Focus };
                for (int i = 1; i < replay.Cast.Count; i++)
                {
                    match.CycleFocus(1);
                    focused.Add(match.Focus);
                }
                match.CycleFocus(1);
                if (!match.Seek(range.First + (uint)screenshotTick)) throw new InvalidOperationException("Seek to the screenshot tick failed.");
                await Task.Yield();
                bool allTicks = played == range.Last - range.First && end == range.Last;
                bool steps = back == end - 10 && forward == end;
                bool everyFighter = replay.Cast.All(m => focused.Contains(m.EntityId));
                bool crows = perchTicks.Count == perchShown.Count;
                return new
                {
                    success = allTicks && steps && everyFighter && crows && errors.Count == 0,
                    track = replay.Track.ToString(), refusal = replay.ExactRefusal?.Code.ToString(),
                    range = new[] { range.First, range.Last }, livePresents = played, end, afterTenStepBacks = back, afterTenStepForwards = forward,
                    focused, perchingTicks = perchTicks.Count, perchingTicksWithVisual = perchShown.Count,
                    pausedAt = replay.CurrentTick, playSeconds = Math.Round(watch.Elapsed.TotalSeconds, 2), playFrames = frames,
                    consoleErrors = errors.Take(20).ToArray(), consoleErrorCount = errors.Count,
                };
            }
            finally
            {
                Application.logMessageReceived -= Log;
            }
        }

        /// <summary>
        /// Over 20 seeded (A, B) pairs, a seek to B straight from the first tick and a seek to B after seeking A and
        /// playing 60 live ticks must show the same census, play no sound during either seek, and play the same
        /// sounds on the next live tick. Reports seek milliseconds.
        /// </summary>
        private static object SeekHygiene(ReplayMatch match)
        {
            var replay = match.Replay!;
            if (match.Transport.State == ReplayTransportState.Playing) match.TogglePlay();
            var range = replay.Range;
            uint lo = range.First + 1, hi = range.Last - 62;
            if (hi <= lo) throw new InvalidOperationException("The Replay is too short for seek pairs.");
            var rng = new System.Random(7);
            var seekMs = new List<double>();
            var failures = new List<object>();
            int warmupPlayed = 0, warmupSuppressed = 0, nextTickSounds = 0;
            var watch = new System.Diagnostics.Stopwatch();
            bool Timed(uint tick)
            {
                watch.Restart();
                bool ok = match.Seek(tick);
                seekMs.Add(watch.Elapsed.TotalMilliseconds);
                return ok;
            }
            (IReadOnlyList<string> Census, int Played, int Suppressed, int Next) SeekB(uint b)
            {
                int played = PresentationOutput.Played, suppressed = PresentationOutput.Suppressed;
                if (!Timed(b)) throw new InvalidOperationException($"Seek to {b} failed.");
                var census = match.Census();
                int seekPlayed = PresentationOutput.Played - played, seekSuppressed = PresentationOutput.Suppressed - suppressed;
                int before = PresentationOutput.Played;
                match.StepForward();
                return (census, seekPlayed, seekSuppressed, PresentationOutput.Played - before);
            }
            for (int pair = 0; pair < 20; pair++)
            {
                uint a = (uint)rng.Next((int)lo, (int)hi), b = (uint)rng.Next((int)lo, (int)hi);
                if (!match.Seek(range.First)) throw new InvalidOperationException("Seek to the first tick failed.");
                var fresh = SeekB(b);
                if (!Timed(a)) throw new InvalidOperationException($"Seek to {a} failed.");
                for (int i = 0; i < 60; i++) match.StepForward();
                var after = SeekB(b);
                warmupPlayed += fresh.Played + after.Played;
                warmupSuppressed += fresh.Suppressed + after.Suppressed;
                nextTickSounds += after.Next;
                var onlyFresh = fresh.Census.Except(after.Census).ToArray();
                var onlyAfter = after.Census.Except(fresh.Census).ToArray();
                if (onlyFresh.Length > 0 || onlyAfter.Length > 0 || fresh.Played != 0 || after.Played != 0 || fresh.Next != after.Next)
                    failures.Add(new { a, b, onlyFresh = onlyFresh.Take(8).ToArray(), onlyAfter = onlyAfter.Take(8).ToArray(),
                        seekSounds = new[] { fresh.Played, after.Played }, nextTickSounds = new[] { fresh.Next, after.Next }, censusLines = fresh.Census.Count });
            }
            var sorted = seekMs.OrderBy(x => x).ToList();
            double Percentile(double p) => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
            return new
            {
                success = failures.Count == 0, pairs = 20, failures = failures.Take(6).ToArray(), failureCount = failures.Count,
                warmupSoundsPlayed = warmupPlayed, warmupSoundsSuppressed = warmupSuppressed, nextTickSoundsAfterHistory = nextTickSounds,
                seeks = sorted.Count, seekMedianMs = Math.Round(Percentile(0.5), 2), seekP95Ms = Math.Round(Percentile(0.95), 2),
                seekMaxMs = Math.Round(sorted[sorted.Count - 1], 2),
            };
        }

        /// <summary>Paused (timeScale 0), orbit mode must still turn and zoom the render camera.</summary>
        private static async Task<object> OrbitCheck(ReplayMatch match)
        {
            if (match.Transport.State == ReplayTransportState.Playing) match.TogglePlay();
            var mount = UnityEngine.Object.FindFirstObjectByType<SlopArena.Client.Camera.CameraMount>()
                ?? throw new InvalidOperationException("The Replay scene has no CameraMount.");
            var orbital = mount.GetComponent<Unity.Cinemachine.CinemachineOrbitalFollow>();
            var camera = mount.RenderCamera;
            mount.SetMode(SlopArena.Client.Camera.CameraMode.Normal);
            await Task.Delay(200);
            var before = camera.transform.position;
            float distanceBefore = Vector3.Distance(before, match.RendererOf(match.Focus)!.transform.position);
            orbital.HorizontalAxis.Value += 60f;
            orbital.RadialAxis.Value = orbital.RadialAxis.Range.y;
            await Task.Delay(600);
            var after = camera.transform.position;
            float distanceAfter = Vector3.Distance(after, match.RendererOf(match.Focus)!.transform.position);
            mount.SetMode(SlopArena.Client.Camera.CameraMode.FreeCursor);
            float moved = Vector3.Distance(before, after);
            return new { success = Time.timeScale == 0f && moved > 0.5f, timeScale = Time.timeScale, moved, distanceBefore, distanceAfter };
        }

        /// <summary>
        /// For each fighter with a blade move, the weapon sampled under a recorded pose at tick 9 must match today's
        /// pose at tick 9 and differ from today's pose at the state's own tick 2.
        /// </summary>
        private static async Task<object> ResumeCheck(ReplayMatch match)
        {
            var replay = match.Replay!;
            if (match.Transport.State == ReplayTransportState.Playing) match.TogglePlay();
            uint target = 0;
            ulong entity = 0;
            for (uint tick = replay.Range.First; tick + 5 < replay.Range.Last; tick++)
            {
                match.Seek(tick);
                foreach (var member in replay.Cast)
                    if (replay.World.GetActiveMove(member.EntityId) is { } move && move.Slot.ClipCount > 1 && move.ClipIndex > 0 && move.Pose.ElapsedTicks > 2
                        && move.Pose.ElapsedTicks + 6 < move.Pose.DurationTicks)
                    { target = tick; entity = member.EntityId; break; }
                if (entity != 0) break;
            }
            if (entity == 0) throw new InvalidOperationException("Fixture needs a multi-phase attack with six remaining ticks.");
            var renderer = match.RendererOf(entity);
            var field = typeof(SlopArena.Client.Entities.PlayerRenderer).GetField("_currentAnimState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            double TimeOfPose() => ((Animancer.AnimancerState)field.GetValue(renderer)).Time;
            var results = new List<object>();
            foreach (bool step in new[] { false, true })
            {
                match.Seek(target);
                if (step) match.StepForward();
                double before = TimeOfPose();
                var activation = replay.World.GetState(entity).AttackSequence;
                uint start = replay.CurrentTick!.Value;
                match.TogglePlay();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (replay.CurrentTick < start + 2 && watch.Elapsed.TotalSeconds < 3) await Task.Delay(1);
                match.TogglePlay();
                double after = TimeOfPose();
                bool same = replay.World.GetState(entity).AttackSequence == activation;
                if (!same || after <= before) throw new InvalidOperationException($"Resume froze or restarted attack. step={step}, {before} -> {after}");
                results.Add(new { step, before, after, sameActivation = same });
            }
            return new { success = true, target, entity, results };
        }

        private static object ScrubClockCheck(ReplayMatch match)
        {
            var replay = match.Replay!;
            var renderer = match.RendererOf(replay.Cast[0].EntityId);
            var slot = renderer.CharacterDef.CookedSlots.First(s => s != null && s.ClipCount > 1);
            var moveClip = slot.Clip(1);
            var state = replay.World.GetState(replay.Cast[0].EntityId);
            state.State = ActionState.Attacking;
            state.AttackSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slot.Ordinal]);
            state.AttackElapsedTicks = 40;
            state.AnimIndex = 0;
            var pose = new AttackPose(moveClip.AnimationId, 4f, 99, true);
            renderer.ApplyServerState(state, slot, new SlopArena.Client.Entities.RecordedFighterClocks(pose, 0, 0, -1, 0));
            if (!renderer.PlayScrubbedState(state, slot.IsAir, 40)) throw new InvalidOperationException("Recorded clip missing.");
            var animation = (Animancer.AnimancerState)typeof(SlopArena.Client.Entities.PlayerRenderer)
                .GetField("_currentAnimState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer);
            var args = new object[] { moveClip.AnimationId, null, null };
            typeof(SlopArena.Client.Entities.PlayerRenderer).GetMethod("TryGetAnimation", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(renderer, args);
            bool success = Math.Abs(animation.Time - 4.0 / 60) < 1e-5 && animation.Clip == (AnimationClip)args[1];
            if (!success) throw new InvalidOperationException($"Recorded phase clock sampled at {animation.Time}.");
            double phaseTime = animation.Time;
            var stretched = new AttackPose(moveClip.AnimationId, 4f, 99, false);
            renderer.ApplyServerState(state, slot, new SlopArena.Client.Entities.RecordedFighterClocks(stretched, 0, 0, -1, 0));
            renderer.PlayScrubbedState(state, slot.IsAir, 40);
            double expected = 4.0 / 60;
            int bakedIndex = renderer.BakedData?.FindAnimIndex(moveClip.AnimationId) ?? -1;
            if (bakedIndex >= 0) expected *= renderer.BakedData.Animations[bakedIndex].FrameCount / 99.0;
            double stretchedTime = animation.Time;
            if (Math.Abs(stretchedTime - expected) > 1e-5)
                throw new InvalidOperationException($"Recorded duration sampled at {stretchedTime}, expected {expected}.");
            match.Seek(replay.CurrentTick!.Value);
            return new { success, phaseTime, stretchedTime, expected, recordedElapsed = 4, moveElapsed = 40 };
        }

        private static object PoseCheck(ReplayMatch match)
        {
            var replay = match.Replay!;
            uint shown = replay.CurrentTick ?? replay.Range.First;
            var results = new List<object>();
            bool success = true;
            try
            {
                foreach (var member in replay.Cast)
                {
                    var renderer = match.RendererOf(member.EntityId);
                    var slot = renderer?.CharacterDef?.CookedSlots?.FirstOrDefault(s => s != null && s.ClipCount > 0
                        && s.Timeline.Stages.Any(stage => stage.ReadHitboxes().Any(e => e.BoneName == "_weapon_hilt")));
                    if (renderer == null || slot == null) continue;
                    var clip = slot.Clip(0);
                    var state = replay.World.GetState(member.EntityId);
                    state.State = ActionState.Attacking;
                    state.AttackSlot = CanonicalSlotProjection.ToWire(CanonicalSlotProjection.All[slot.Ordinal]);
                    state.AnimIndex = 0;
                    state.ComboStage = (byte)clip.StageIndex;
                    state.IsGrounded = !slot.IsAir;
                    state.AttackElapsedTicks = (ushort)(clip.StartTick + 2);
                    var later = state;
                    later.AttackElapsedTicks = (ushort)(clip.StartTick + 9);
                    var recorded = new SlopArena.Client.Entities.RecordedFighterClocks(
                        new AttackPose(clip.AnimationId, 9f, clip.DurationTicks, clip.IsPhaseClip), 0f, 0f, -1, 0f);
                    renderer.ApplyServerState(state, slot, null);
                    bool a = renderer.TrySampleBakedWeaponPath(state, slot.IsAir, out _, out var todayAt2);
                    renderer.ApplyServerState(later, slot, null);
                    bool b = renderer.TrySampleBakedWeaponPath(later, slot.IsAir, out _, out var todayAt9);
                    renderer.ApplyServerState(state, slot, recorded);
                    bool r = renderer.TrySampleBakedWeaponPath(state, slot.IsAir, out _, out var recordedAt9);
                    bool follows = a && b && r && (recordedAt9 - todayAt9).magnitude < 1e-3f && (recordedAt9 - todayAt2).magnitude > 1e-3f;
                    success &= follows;
                    results.Add(new { member.EntityId, move = slot.Id, clip = clip.AnimationId, follows,
                        recordedVsToday9 = (recordedAt9 - todayAt9).magnitude, recordedVsToday2 = (recordedAt9 - todayAt2).magnitude });
                }
            }
            finally
            {
                match.Seek(shown);
            }
            if (results.Count == 0) throw new InvalidOperationException("No fighter in this Replay has a blade move.");
            return new { success, fighters = results };
        }

        /// <summary>True when the display world has a perching shape; <paramref name="shown"/> says each has a projectile visual.</summary>
        private static bool ShowsPerching(ServerSimulation world, ProjectileVFXManager? projectiles, out bool shown)
        {
            var perches = world.Resolver.GetActiveHitboxes().Where(h => h.Perch.HasValue).Select(h => h.EmissionId).ToList();
            shown = false;
            if (perches.Count == 0) return false;
            var visuals = projectiles == null ? null
                : typeof(ProjectileVFXManager).GetField("_activeVisuals", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(projectiles) as IDictionary;
            if (visuals == null) return true;
            var emissions = new HashSet<ulong>();
            foreach (DictionaryEntry entry in visuals)
                if (entry.Value is GameObject go && go != null && go.activeInHierarchy)
                    emissions.Add((ulong)entry.Key.GetType().GetField("Item4")!.GetValue(entry.Key)!);
            shown = perches.All(emissions.Contains);
            return true;
        }

        private static object Describe(ReplayMatch match)
        {
            var replay = match.Replay!;
            return new
            {
                success = true, track = replay.Track.ToString(), refusal = replay.ExactRefusal?.Code.ToString(),
                range = new[] { replay.Range.First, replay.Range.Last }, tick = replay.CurrentTick, focus = match.Focus,
                cast = replay.Cast.Select(m => new { m.EntityId, package = m.Content.Identity.PackageId, kind = m.Kind.ToString() }).ToArray(),
                conclusion = ReplayText.Conclusion(replay.Conclusion, replay.PrefixReason),
            };
        }

        internal static ReplayMatch? ActiveReplay()
            => MatchBase.Active is ReplayMatch { Replay: not null } match ? match : null;

        internal static async Task<T> WaitFor<T>(Func<T?> probe, double seconds, Func<string> failure) where T : class
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                if (probe() is { } value) return value;
                if (ReplayLaunch.LastFailure != null) throw new InvalidOperationException(ReplayLaunch.LastFailure);
                await Task.Delay(15);
            }
            throw new TimeoutException(failure());
        }

        private static void RequirePlayMode()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("This command needs Play Mode (editor_play first).");
        }

        internal static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
    }
}

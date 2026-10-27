#if UNITY_EDITOR
using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SlopArena.Client.Input;
using SlopArena.Client.UI;
using SlopArena.Client.World;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlopArena.EditorTools
{
    public static class SlopArenaMayhemCommands
    {
        private const string Key = "SlopArena.Mayhem.DevSession";
        private sealed class Saved
        {
            public MatchFormat format;
            public GameMode mode;
            public CharacterClass fighter;
            public string package, arena;
            public SoloSeat[] seats;
            public uint seed;
            public ulong entity;
        }

        [MenuItem("Tools/SlopArena/Manki Mayhem/Prepare Local Play (3 CPUs)")]
        private static void MenuPrepare() => Prepare();
        [MenuItem("Tools/SlopArena/Manki Mayhem/Restore Editor after Stop")]
        private static void MenuRestore() => Restore();

        [CliCommand("sloparena.mayhem.prepare", "Prepare offline Manki Mayhem with Solo seats; press Play afterward. Restore after Stop.", MainThreadRequired = true, Tags = new[] { "mayhem" })]
        public static object Prepare(
            [CliArg("cpus", "One to three CPU opponents.")] int cpus = 3,
            [CliArg("fighter", "Admitted fighter name.")] string fighter = "fightguy",
            [CliArg("seed", "Nonzero Shared director seed.")] uint seed = 41,
            [CliArg("couch", "Replace CPU seats with spare connected pads; the current menu pad stays P1.")] bool couch = false)
        {
            if (cpus < 1 || cpus > 3 || seed == 0 || !Enum.TryParse(fighter, true, out CharacterClass selected) || selected == CharacterClass.None)
                throw new ArgumentException("Expected 1–3 CPUs, nonzero seed, and an admitted fighter name.");
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) throw new InvalidOperationException("Restore the preceding Mayhem session first.");
            var saved = new Saved { format = MatchConfig.Format, mode = MatchConfig.Mode, fighter = MatchConfig.PlayerClass,
                package = MatchConfig.PlayerPackageId, arena = MatchConfig.ArenaName, seed = MatchConfig.MayhemSeed,
                seats = (SoloSeat[])MatchConfig.SoloSeats.Clone(), entity = MatchConfig.LocalEntityId };
            SlopArenaStageViewCommands.Prepare("manki_mayhem");
            SessionState.SetString(Key, JsonConvert.SerializeObject(saved));
            var driver = GameObject.Find("BaselineCaptureDriver");
            if (driver != null) UnityEngine.Object.DestroyImmediate(driver);
            MatchConfig.Mode = GameMode.Solo;
            MatchConfig.Format = MatchFormat.MankiMayhem;
            MatchConfig.MayhemSeed = seed;
            MatchConfig.PlayerClass = selected;
            MatchConfig.PlayerPackageId = fighter.ToLowerInvariant();
            MatchConfig.ArenaName = "manki_mayhem";
            MatchConfig.LocalEntityId = 1;
            var roster = new[] { CharacterClass.Manki, CharacterClass.Wibou, CharacterClass.Bashie };
            for (int player = 2; player <= 4; player++)
                MatchConfig.Seat(player) = new SoloSeat { Kind = player <= cpus + 1 ? SeatKind.Cpu : SeatKind.Open,
                    Fighter = roster[player - 2], Difficulty = CpuDifficulty.Normal };
            if (couch)
            {
                var primary = CouchPads.P1MenuPad ?? Gamepad.current;
                foreach (var pad in Gamepad.all.Where(p => p != primary)) MatchConfig.JoinSoloSeat(pad.deviceId);
            }
            return new { success = true, stage = "manki_mayhem", fighter, cpus, seed, couch,
                seats = MatchConfig.SoloSeats, next = "editor_play; after editor_stop use sloparena.mayhem.restore" };
        }

        private static float[] V(Vector3 value) => new[] { value.x, value.y, value.z };

        [CliCommand("sloparena.mayhem.inspect", "Read the live offline director, scores, rings and host bombs.", MainThreadRequired = true, Tags = new[] { "mayhem" })]
        public static object Inspect()
        {
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            var sim = match != null ? match.CaptureSimulation : null;
            if (!MatchConfig.IsLocalMayhem || sim?.MankiMayhem == null) throw new InvalidOperationException("No running local Mayhem match.");
            var camera = UnityEngine.Object.FindFirstObjectByType<SlopArena.Client.Camera.CameraMount>();
            var main = camera.RenderCamera;
            var host = GameObject.Find("Manki Host (presentation only)");
            return new { success = true, camera = new { position = V(main.transform.position), angles = V(main.transform.eulerAngles),
                main.fieldOfView, main.aspect, radius = camera.GetOrbitRadius(), pitch = camera.GetCameraPitchDeg(),
                deckFront = V(main.WorldToViewportPoint(new Vector3(0, 0, -10))), deckBack = V(main.WorldToViewportPoint(new Vector3(0, 0, 10))) },
                host = host.GetComponentsInChildren<Renderer>().Select(r => new { r.name, center = V(r.bounds.center), size = V(r.bounds.size) }).ToArray(),
                fighters = sim.GetAllStates().Select(p => new { id = p.Key.ToString(),
                    position = new[] { p.Value.PX, p.Value.PY, p.Value.PZ },
                    state = p.Value.State.ToString(), p.Value.IsGrounded }).ToArray(),
                tick = sim.CurrentTick, director = sim.MankiMayhem, players = sim.MayhemPlayers,
                rings = sim.MayhemLandingRings, bombs = sim.Resolver.GetActiveHitboxes().Where(h => h.OwnerId == ServerSimulation.MayhemHostEntityId)
                    .Select(h => new { h.X, h.Y, h.Z, h.AttachedEntityId, h.FuseTicksLeft }).ToArray() };
        }

        [CliCommand("sloparena.mayhem.capture-begin", "Start a disposable scripted P1 plus three real CPUs capture on the live match.", MainThreadRequired = true, Tags = new[] { "mayhem", "capture" })]
        public static object CaptureBegin([CliArg("output", "Fresh repository-relative directory under tmp/.", Required = true)] string output,
            [CliArg("show", "Record the full show and all host/CRT/booth reactions.")] bool show = false)
        {
            if (!EditorApplication.isPlaying || !MatchConfig.IsLocalMayhem) throw new InvalidOperationException("Start local Mayhem first.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string path = Path.GetFullPath(Path.Combine(root, output));
            if (!path.StartsWith(Path.Combine(root, "tmp") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Directory.Exists(path)) throw new ArgumentException("Use a fresh output directory under tmp/.");
            Directory.CreateDirectory(Path.Combine(path, "round"));
            var match = UnityEngine.Object.FindFirstObjectByType<TrainingMatch>();
            var host = new GameObject("Mayhem verification capture"); host.SetActive(false);
            var capture = host.AddComponent<MankiMayhemCapture>(); capture.Match = match; capture.Output = path; capture.FullShow = show;
            capture.Host = (typeof(TrainingMatch).GetField("_mayhem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(match) as MankiMayhemPresentation)?.Host;
            host.SetActive(true);
            var viewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            Resources.FindObjectsOfTypeAll(viewType).OfType<EditorWindow>().FirstOrDefault()?.Focus();
            return new { success = true, output = path };
        }

        [CliCommand("sloparena.mayhem.advance", "Wait a bounded live capture chunk, at most 240 ticks and 25 seconds; the real match keeps ticking.", MainThreadRequired = true, Tags = new[] { "mayhem", "capture" })]
        public static async Task<object> Advance([CliArg("ticks", "Maximum live ticks to observe, 1..240.")] int ticks = 180)
        {
            var capture = UnityEngine.Object.FindFirstObjectByType<MankiMayhemCapture>();
            if (capture == null || ticks < 1 || ticks > 240) throw new ArgumentException("Start capture and request 1..240 ticks.");
            uint start = capture.Match.CaptureSimulation.CurrentTick;
            double deadline = EditorApplication.timeSinceStartup + 25;
            while (!capture.Done && capture.Match.CaptureSimulation.CurrentTick - start < ticks && EditorApplication.timeSinceStartup < deadline)
                await Task.Delay(20);
            return new { success = true, tick = capture.Match.CaptureSimulation.CurrentTick, capture.Done,
                director = capture.Match.CaptureSimulation.MankiMayhem };
        }

        [CliCommand("sloparena.mayhem.online-wait", "Observe a bounded local full-stack milestone without driving gameplay.", MainThreadRequired = true, Tags = new[] { "mayhem", "netplay" })]
        public static async Task<object> OnlineWait([CliArg("until", "auth, room, stage, fighter, ready, scatter, jump, breakout or results.", Required = true)] string until)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            if (!new[] { "auth", "room", "stage", "fighter", "ready", "scatter", "jump", "breakout", "results" }.Contains(until))
                throw new ArgumentException("Unknown online milestone.");
            double deadline = EditorApplication.timeSinceStartup + 20;
            do
            {
                var chat = SlopArena.Client.Network.ChatSession.Instance;
                var page = FrontendController.CurrentPage;
                bool frontend = FrontendController.IsFrontendActive;
                bool done = until switch
                {
                    "auth" => chat?.ActiveLobby?.IsConnected == true && !chat.NeedsDisplayName,
                    "stage" => frontend && page == FrontendPage.StageSelect,
                    "fighter" => frontend && page == FrontendPage.FighterSelect,
                    "ready" => frontend && FrontendController.CurrentContext?.Q<UnityEngine.UIElements.Button>("btn-start-match")?.enabledInHierarchy == true,
                    "scatter" => OnlineSimulation()?.MankiMayhem is { Card: MankiMayhemCard.Scatter, Phase: MankiMayhemPhase.Call, PhaseTick: >= 40 },
                    "jump" => OnlineSimulation()?.MankiMayhem is { Card: MankiMayhemCard.Jump, Phase: MankiMayhemPhase.Call, PhaseTick: >= 20 },
                    "breakout" => OnlineSimulation()?.MankiMayhem is { Card: MankiMayhemCard.BreakOut, Phase: MankiMayhemPhase.Call, PhaseTick: >= 20 },
                    "results" => frontend && page == FrontendPage.Results && SlopArena.Client.ClientSession.CurrentMatchResults?.Mayhem != null,
                    _ => false,
                };
                if (until == "room" && frontend && page == FrontendPage.LobbyRoom && chat?.ActiveLobby != null)
                    done = (await chat.ActiveLobby.GetMyRoomAsync())?.Members.Length >= 2;
                if (done) return new { success = true, done, until, page = page.ToString(),
                    tick = OnlineSimulation()?.CurrentTick, director = OnlineSimulation()?.MankiMayhem };
                await Task.Delay(until == "room" ? 1500 : 50);
            } while (EditorApplication.timeSinceStartup < deadline);
            if (until == "results") return new { success = true, done = false, until };
            throw new TimeoutException($"Online milestone '{until}' was not reached in 20 seconds.");
        }

        [CliCommand("sloparena.mayhem.online-observe", "Observe up to 20 seconds, saving JUMP! and BREAK OUT! calls regardless of bag order.", MainThreadRequired = true, Tags = new[] { "mayhem", "capture", "netplay" })]
        public static async Task<object> OnlineObserve([CliArg("output", "Evidence directory under tmp/.", Required = true)] string output)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string path = Path.GetFullPath(Path.Combine(root, output));
            if (!path.StartsWith(Path.Combine(root, "tmp") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Use a directory under tmp/.");
            Directory.CreateDirectory(path);
            double deadline = EditorApplication.timeSinceStartup + 20;
            while (EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline)
            {
                var sim = OnlineSimulation();
                if (sim?.MankiMayhem is { Phase: MankiMayhemPhase.Call, PhaseTick: >= 30 } state && sim.CurrentTick > 380
                    && state.Card is MankiMayhemCard.Scatter or MankiMayhemCard.Jump or MankiMayhemCard.BreakOut)
                {
                    string name = "online-" + state.Card.ToString().ToLowerInvariant();
                    if (!File.Exists(Path.Combine(path, name + ".png")))
                    {
                        var observed = new { tick = sim.CurrentTick, director = state, players = sim.MayhemPlayers, walls = sim.MayhemWalls };
                        await SlopArenaCaptureCommands.Capture(output + "/" + name + ".png");
                        File.WriteAllText(Path.Combine(path, name + ".json"), JsonConvert.SerializeObject(observed, Formatting.Indented));
                    }
                }
                if (FrontendController.IsFrontendActive && FrontendController.CurrentPage == FrontendPage.Results
                    && SlopArena.Client.ClientSession.CurrentMatchResults?.Mayhem != null)
                    return new { success = true, done = true, output };
                await Task.Delay(50);
            }
            return new { success = true, done = false, output, tick = OnlineSimulation()?.CurrentTick };
        }

        private static ServerSimulation OnlineSimulation()
        {
            var match = UnityEngine.Object.FindFirstObjectByType<PvPMatch>();
            if (match == null) return null;
            var bridge = typeof(PvPMatch).GetField("_bridge", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(match) as SlopArena.Client.Simulation.RollbackSimulationBridge;
            return bridge?.Simulation;
        }

        [CliCommand("sloparena.mayhem.restore", "Restore scene and local configuration after stopped Mayhem verification.", MainThreadRequired = true, Tags = new[] { "mayhem" })]
        public static object Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before restoring Mayhem.");
            var saved = JsonConvert.DeserializeObject<Saved>(SessionState.GetString(Key, ""));
            if (saved == null) return new { success = true, restored = false };
            SlopArenaStageViewCommands.Restore();
            MatchConfig.Format = saved.format; MatchConfig.Mode = saved.mode;
            MatchConfig.PlayerClass = saved.fighter; MatchConfig.PlayerPackageId = saved.package;
            MatchConfig.ArenaName = saved.arena; MatchConfig.MayhemSeed = saved.seed; MatchConfig.LocalEntityId = saved.entity;
            Array.Copy(saved.seats, MatchConfig.SoloSeats, 3);
            SessionState.EraseString(Key);
            return new { success = true, restored = true };
        }
    }
}
#endif

using System.Runtime.InteropServices;
using SlopArena.Shared;

namespace SlopArena.Server
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Any(arg => arg.StartsWith("--steam-probe-server", StringComparison.Ordinal)))
            {
                if (args.Length != 2 || args[0] != "--steam-probe-server")
                {
                    Console.Error.WriteLine("Usage: SlopArena.Server --steam-probe-server <allowed-client-steamid>");
                    Environment.ExitCode = 2;
                    return;
                }

                Environment.ExitCode = await SteamProbeServer.RunAsync(args[1]);
                return;
            }

            Console.WriteLine("=== SLOPArena Game Server ===");

            // Load configuration
            string configPath = args.Length > 0 ? args[0] : "server.json";
            var config = ServerConfig.Load(configPath);
            Console.WriteLine($"Server: {config.ServerName}");
            Console.WriteLine($"Region: {config.Region}");
            Console.WriteLine($"Port range: {config.Port}-{config.Port + config.MaxConcurrentMatches - 1}");
            Console.WriteLine($"Max concurrent matches: {config.MaxConcurrentMatches}");
            Console.WriteLine($"Master server: {config.MasterServerUrl}");
            Console.WriteLine($"Arena data: {config.ArenaDataDir}");
            Console.WriteLine();

            // Gameplay arenas are loaded from the published .arena files.
            ArenaRegistry.LoadFromDirectory(config.ArenaDataDir);

            using var cts = new CancellationTokenSource();
            await using var steamHost = config.IsVps ? new SteamGameServerHost(config.MaxConcurrentMatches) : null;
            if (steamHost is not null)
                await steamHost.StartAsync(cts.Token);

            var orchestrator = new MultiMatchOrchestrator(config, steamHost);
            steamHost?.AttachOrchestrator(orchestrator);
            bool contentReady = orchestrator.ContentReady && ArenaRegistry.Get("slop_pit").HasValue;
            if (!contentReady)
            {
                Console.WriteLine("[Content] Required cooked roster or default arena unavailable; readiness disabled.");
                orchestrator.StopAcceptingMatches();
            }
            var registration = new GameServerRegistration(config, orchestrator,
                getSteamId: steamHost is null ? null : () => steamHost.IsReady ? steamHost.CurrentSteamId : 0);

            orchestrator.ReportMatchResult = (matchId, winner) =>
                _ = registration.ReportMatchResultAsync(matchId, winner);
            orchestrator.ReportMatchCancellation = registration.QueueMatchCancellation;

            using var control = new MatchControlServer(orchestrator, config.Port,
                defaultArena: "slop_pit", controlKey: config.IsVps ? config.MatchControlKey : null,
                isReady: () => contentReady && registration.CanAdmitMatches, steamHost: steamHost);
            control.Start();

            void BeginShutdown()
            {
                orchestrator.StopAcceptingMatches();
                control.Stop();
                cts.Cancel();
            }

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                BeginShutdown();
            };
            using var sigterm = OperatingSystem.IsWindows() ? null :
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, e =>
                {
                    e.Cancel = true;
                    BeginShutdown();
                });

            Console.WriteLine("Registering with master server (recovery enabled).");
            try
            {
                await registration.RunAsync(cts.Token);
            }
            finally
            {
                BeginShutdown();
                orchestrator.Shutdown();
                Console.WriteLine("Server stopped.");
            }

        }
    }
}

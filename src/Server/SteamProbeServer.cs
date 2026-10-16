using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Steamworks;

namespace SlopArena.Server;

static class SteamProbeServer
{
    const int MessageSize = 200;
    const int MaxMessagesPerTick = 32;

    public static async Task<int> RunAsync(string allowedClientSteamIdText)
    {
        if (!ulong.TryParse(allowedClientSteamIdText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong allowedClientSteamId) ||
            !new CSteamID(allowedClientSteamId).IsValid())
        {
            Console.Error.WriteLine("Invalid --steam-probe-server SteamID.");
            return 2;
        }

        if (GameServer.InitEx(0, 0, 0, EServerMode.eServerModeAuthentication, "1.0.0.0", out string initError) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        {
            Console.Error.WriteLine($"Steam game-server initialization failed: {initError}");
            return 1;
        }

        HSteamListenSocket listenSocket = HSteamListenSocket.Invalid;
        HSteamNetConnection activeConnection = HSteamNetConnection.Invalid;
        var cancel = new CancellationTokenSource();
        using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, e =>
        {
            e.Cancel = true;
            cancel.Cancel();
        });
        bool loggedOn = false;
        bool loginFailed = false;
        bool connectedLogged = false;
        long connectingAt = 0;
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += cancelHandler;

        using var loginSuccess = Callback<SteamServersConnected_t>.CreateGameServer(_ => loggedOn = true);
        using var loginFailure = Callback<SteamServerConnectFailure_t>.CreateGameServer(_ => loginFailed = true);
        using var connectionStatus = Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(change =>
        {
            var conn = change.m_hConn;
            var info = change.m_info;
            if (info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                if (conn == activeConnection)
                {
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Steam probe peer closed", false);
                    activeConnection = HSteamNetConnection.Invalid;
                    connectedLogged = false;
                }
                return;
            }

            ulong remoteId = info.m_identityRemote.GetSteamID64();
            if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                // Steam may not report the authenticated peer identity until Connected.
                // No application message is read before that identity is checked.
                if (activeConnection != HSteamNetConnection.Invalid || !loggedOn ||
                    (remoteId != 0 && remoteId != allowedClientSteamId))
                {
                    Console.Error.WriteLine($"[SteamProbe] rejected connecting peer={remoteId} expected={allowedClientSteamId} busy={activeConnection != HSteamNetConnection.Invalid} logged-on={loggedOn}");
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Steam probe connection rejected", false);
                    return;
                }
                var accept = SteamGameServerNetworkingSockets.AcceptConnection(conn);
                if (accept != EResult.k_EResultOK)
                {
                    Console.Error.WriteLine($"[SteamProbe] accept failed peer={remoteId} result={accept}");
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Steam probe connection rejected", false);
                    return;
                }
                activeConnection = conn;
                connectingAt = Stopwatch.GetTimestamp();
            }
            else if (info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected && conn == activeConnection)
            {
                if (remoteId != allowedClientSteamId)
                {
                    Console.Error.WriteLine($"[SteamProbe] rejected connected peer={remoteId} expected={allowedClientSteamId}");
                    SteamGameServerNetworkingSockets.CloseConnection(conn, 0, "Steam probe identity rejected", false);
                    activeConnection = HSteamNetConnection.Invalid;
                    return;
                }
                if (connectedLogged)
                    return;
                connectedLogged = true;
                string route = info.m_idPOPRelay.m_SteamNetworkingPOPID == 0
                    ? "unknown" : $"relay-pop-{info.m_idPOPRelay}";
                double connectMs = (Stopwatch.GetTimestamp() - connectingAt) * 1000.0 / Stopwatch.Frequency;
                Console.WriteLine($"[SteamProbe] connected client={remoteId} route={route} connection_ms={connectMs:F0}");
            }
        });

        try
        {
            SteamGameServer.SetProduct("sloparena-steam-probe");
            SteamGameServer.SetModDir("sloparena");
            SteamGameServer.SetGameDescription("SLOPArena isolated SteamNetworkingSockets probe");
            SteamGameServer.SetDedicatedServer(true);
            SteamGameServer.LogOnAnonymous();
            Console.WriteLine("[SteamProbe] waiting for authenticated game-server login.");

            long loginDeadline = Stopwatch.GetTimestamp() + 30 * Stopwatch.Frequency;
            while (!cancel.IsCancellationRequested && !loggedOn && !loginFailed &&
                   Stopwatch.GetTimestamp() < loginDeadline)
            {
                GameServer.RunCallbacks();
                await Task.Delay(10, cancel.Token);
            }
            if (loginFailed || !loggedOn || !SteamGameServer.BLoggedOn())
            {
                Console.Error.WriteLine("Steam game-server login failed.");
                return 1;
            }

            Console.WriteLine($"[SteamProbe] server_steamid={(ulong)SteamGameServer.GetSteamID()}");
            listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, 0, Array.Empty<SteamNetworkingConfigValue_t>());
            if (listenSocket == HSteamListenSocket.Invalid)
            {
                Console.Error.WriteLine("Steam probe listener creation failed.");
                return 1;
            }
            Console.WriteLine("[SteamProbe] listening on Steam virtual port 0.");

            var messages = new IntPtr[MaxMessagesPerTick];
            while (!cancel.IsCancellationRequested)
            {
                GameServer.RunCallbacks();
                if (activeConnection != HSteamNetConnection.Invalid && connectedLogged)
                {
                    int count = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(activeConnection, messages, messages.Length);
                    if (count < 0)
                    {
                        Console.Error.WriteLine($"[SteamProbe] receive failed: {count}");
                        return 1;
                    }
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr messagePointer = messages[i];
                        try
                        {
                            var message = Marshal.PtrToStructure<SteamNetworkingMessage_t>(messagePointer);
                            if (message.m_cbSize == MessageSize && message.m_conn == activeConnection)
                            {
                                SteamGameServerNetworkingSockets.SendMessageToConnection(activeConnection, message.m_pData, (uint)message.m_cbSize,
                                    Constants.k_nSteamNetworkingSend_Unreliable, out _);
                            }
                        }
                        finally
                        {
                            SteamNetworkingMessage_t.Release(messagePointer);
                        }
                    }
                }
                await Task.Delay(8, cancel.Token);
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            if (activeConnection != HSteamNetConnection.Invalid)
                SteamGameServerNetworkingSockets.CloseConnection(activeConnection, 0, "Steam probe shutdown", false);
            if (listenSocket != HSteamListenSocket.Invalid)
                SteamGameServerNetworkingSockets.CloseListenSocket(listenSocket);
            SteamGameServer.LogOff();
            GameServer.Shutdown();
            cancel.Dispose();
            Console.WriteLine("[SteamProbe] stopped.");
        }
    }
}

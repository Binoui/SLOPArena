using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading;
using Unity.Pipeline.Editor.Commands;
using Unity.Pipeline.Editor.Commands.PackageManager;
using Unity.Pipeline.Editor.Testing;
using UnityEditor;
using UnityEngine;
using Unity.Pipeline.Models;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Threading;
using UnityEditor.MPE;

namespace Unity.Pipeline.Editor
{
    /// <summary>
    /// Automatically starts Pipeline HTTP server when Unity Editor loads.
    /// Handles startup, domain reload persistence, and cleanup.
    ///
    /// Static owner of the live editor server: a static owner survives domain reloads cleanly
    /// (re-created by [InitializeOnLoad]), whereas a ScriptableObject's lifetime does not track the
    /// server across editor events. <see cref="EditorPipelineManager"/> is an optional, inspectable
    /// settings asset whose config is read here at start.
    /// </summary>
    [InitializeOnLoad]
    public static class PipelineServerStartup
    {
        private static EditorPipelineServer m_Server;

        /// <summary>
        /// The live editor pipeline server instance (null when stopped). Exposed so the test guard
        /// can disable its watchdog for a test run and the EditorPipelineManager inspector can read
        /// live status.
        /// </summary>
        public static EditorPipelineServer Server => m_Server;

        static PipelineServerStartup()
        {
            // Don't start server in AssetImportWorker processes
            if (!IsMainProcess())
                return;
            // Setup command discovery using TypeCache for fast Editor performance
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());

            // Clean up any stale instance descriptor files from previous sessions
            CleanupStaleDescriptors();

            // Start the pipeline server (respecting the settings asset's autoStart if one exists)
            if (EditorPipelineManager.Load()?.AutoStart ?? true)
                StartServer();

            // Handle domain reloads and editor shutdown
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;

            // Handle domain reload detection
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        public static void EnsureServerStarted()
        {
            StartServer();
        }

        /// <summary>
        /// Force a clean restart of the editor pipeline server. Unlike EnsureServerStarted, this
        /// works even when the current server's listener has died but still reports IsRunning
        /// (e.g. after a test disrupted it). Used by tests to revive the live server they disrupted.
        /// </summary>
        public static void RestartServer()
        {
            StopServer();
            StartServer();
        }

        [MenuItem("Window/Pipeline/Start Server")]
        private static void MenuStartServer()
        {
            StartServer();
            if (m_Server != null && m_Server.IsRunning)
                Debug.Log($"Pipeline Server started on port {m_Server.Port}");
            else
                Debug.LogWarning("Pipeline Server failed to start");
        }

        [MenuItem("Window/Pipeline/Start Server", true)]
        private static bool MenuStartServerValidate() => m_Server == null || !m_Server.IsRunning;

        [MenuItem("Window/Pipeline/Stop Server")]
        private static void MenuStopServer()
        {
            StopServer();
        }

        [MenuItem("Window/Pipeline/Stop Server", true)]
        private static bool MenuStopServerValidate() => m_Server != null && m_Server.IsRunning;

        [MenuItem("Window/Pipeline/Recover Editor Lease...")]
        private static void MenuRecoverEditorLease()
        {
            if (Application.isBatchMode || EditorCommandOwnershipContext.Current != null)
            {
                Debug.LogWarning("Editor lease recovery requires a human using the Pipeline menu.");
                return;
            }

            const string title = "Recover Editor Lease";
            var ownership = EditorCommandOwnershipSession.GetOrCreate();
            var confirmation = ownership.GetRecoveryConfirmation();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !confirmation.HasValue)
            {
                var status = ownership.GetStatus();
                EditorUtility.DisplayDialog(title,
                    $"No settled lease can be recovered. State: {status.state}; active operations: {status.activeOperations}.\n" +
                    (status.reason ?? "Wait for compilation, imports and admitted work to settle. Unknown completion requires an Editor restart."),
                    "OK");
                return;
            }

            var expected = confirmation.Value;
            if (!EditorUtility.DisplayDialog(title,
                    $"Revoke this idle lease?\n\nSession: {expected.EditorSessionId}\n" +
                    $"Owner: {expected.TerminalHandle}\nIncarnation: {expected.IncarnationId}\nBatch: {expected.BatchId}\n\n" +
                    "The previous gateway will lose access. This does not cancel work or restart the Editor.",
                    "Recover lease", "Cancel"))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorUtility.DisplayDialog(title, "The Editor became busy. The lease was not recovered.", "OK");
                return;
            }
            if (!ownership.TryRecoverSettledLease(expected, out var recovered))
            {
                EditorUtility.DisplayDialog(title,
                    $"The lease changed or work started while confirming. Nothing was revoked.\n" +
                    $"State: {recovered.state}; active operations: {recovered.activeOperations}.", "OK");
                return;
            }
            Debug.Log($"Recovered settled Pipeline lease for {expected.TerminalHandle}, batch {expected.BatchId}; previous token revoked.");
        }

        /// <summary>
        /// Select the EditorPipelineManager settings asset, creating it under Assets/Settings/Pipeline
        /// on first use (the live server otherwise runs from built-in defaults).
        /// </summary>
        [MenuItem("Window/Pipeline/Settings...")]
        private static void OpenSettings()
        {
            var mgr = EditorPipelineManager.Load();
            if (mgr == null)
            {
                const string folder = "Assets/Settings/Pipeline";
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                        AssetDatabase.CreateFolder("Assets", "Settings");
                    AssetDatabase.CreateFolder("Assets/Settings", "Pipeline");
                }

                mgr = ScriptableObject.CreateInstance<EditorPipelineManager>();
                AssetDatabase.CreateAsset(mgr, folder + "/EditorPipelineManager.asset");
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = mgr;
            EditorGUIUtility.PingObject(mgr);
        }

        internal static bool IsMainProcess()
        {
            // Check command line arguments for asset import worker indicators
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-readonly" || args[i] == "--virtual-project-clone")
                    return true;
            }

            if (AssetDatabase.IsAssetImportWorkerProcess())
                return false;

            if (ProcessService.level != ProcessLevel.Main)
                return false;

            return true;
        }

        /// <summary>
        /// Start the Pipeline HTTP server, reading configuration from the EditorPipelineManager
        /// settings asset if one exists (otherwise using defaults).
        /// </summary>
        private static void StartServer()
        {
            if (m_Server != null && m_Server.IsRunning)
                return;

            try
            {
                var cfg = EditorPipelineManager.Load();
                var ownership = EditorCommandOwnershipSession.Load();
                EditorCommandOwnershipSession.Reconcile(ownership);
                m_Server = new EditorPipelineServer(ownership)
                {
                    WatchdogEnabled = cfg?.WatchdogEnabled ?? true,
                    WatchdogIntervalSeconds = cfg?.WatchdogIntervalSeconds ?? 5,
                    LogRequestsResponses = cfg?.LogRequestsResponses ?? false
                };
                m_Server.PersistOwnershipForReload();
                // Rotate the transaction log once per Unity session (main thread; SessionState-gated).
                // The append path runs off-thread and can't touch SessionState.
                PipelineTransactionLog.RotateForNewSession();

                m_Server.Start(cfg?.Port ?? 0); // 0 auto-assigns from the 7800-7849 range.

                // Restore whatever auto-tick state the user last set this session (survives the
                // domain reload that just wiped AutoTickCommand's statics). Only a session with no
                // prior explicit set_autotick call falls back to a default, and that default is "on"
                // when the watchdog is enabled: the watchdog rides EditorApplication.update, but a
                // backgrounded/idle editor stops ticking once the listener dies (no requests left to
                // wake it) — the exact moment the watchdog must run. Keeping auto-tick on by default
                // keeps the update loop spinning regardless of focus, which keeps both the watchdog
                // AND the dispatcher message pump alive.
                Commands.AutoTickCommand.RestoreFromSession(defaultEnabled: m_Server.WatchdogEnabled);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to start Pipeline Server: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the Pipeline HTTP server.
        /// </summary>
        public static void StopServer()
        {
            if (m_Server != null)
            {
                try
                {
                    m_Server.PersistOwnershipForReload();
                    m_Server.Stop();
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Error stopping Pipeline Server: {ex.Message}");
                }
                finally
                {
                    m_Server = null;
                }
            }
        }

        /// <summary>
        /// Clean up stale instance descriptor files from previous Editor sessions.
        /// </summary>
        private static void CleanupStaleDescriptors()
        {
            var projectPath = System.IO.Path.GetDirectoryName(Application.dataPath);

            // Try to read existing descriptor
            var existing = InstanceDescriptor.ReadFromProjectRoot(projectPath);
            if (existing != null)
            {
                // Check if process is still running
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(existing.Pid);
                    if (process.HasExited)
                    {
                        // Process is dead, remove stale file
                        InstanceDescriptor.RemoveFromProjectRoot(projectPath);
                    }
                }
                catch
                {
                    // Process doesn't exist or access denied, remove stale file
                    InstanceDescriptor.RemoveFromProjectRoot(projectPath);
                }
            }
        }

        /// <summary>
        /// Handle play mode state changes.
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Server continues running through play mode changes
            // Status endpoint will reflect current play mode via EditorApplication.isPlaying
        }

        /// <summary>
        /// Handle Editor shutdown.
        /// </summary>
        private static void OnEditorQuitting()
        {
            StopServer(); // Stop() shuts down the server's own dispatcher.
        }

        /// <summary>
        /// Handle before assembly reload (domain reload).
        /// </summary>
        private static void OnBeforeAssemblyReload()
        {
            // Stop serving first: a request that is still waiting for the main thread when the
            // domain unloads deadlocks the Editor. The descriptor stays; the next domain's
            // [InitializeOnLoad] restarts the server and rewrites it.
            m_Server?.StopForReload();
            EditorCommandOwnershipSession.PersistCurrent();
        }

        /// <summary>
        /// Handle after assembly reload (domain reload).
        /// </summary>
        private static void OnAfterAssemblyReload()
        {
            // Server should already be restarted via [InitializeOnLoad]
            // This is mainly for logging/verification
        }
    }
    internal static class EditorCommandOwnershipSession
    {
        private const string SessionIdKey = "Unity.Pipeline.EditorOwnership.SessionId";
        private const string StateKey = "Unity.Pipeline.EditorOwnership.State";
        private static readonly int s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        private static readonly object s_ServersGate = new object();
        private static readonly List<EditorPipelineServer> s_Servers = new List<EditorPipelineServer>();
        private static EditorCommandOwnership s_Ownership;
        private static EditorCommandOwnership.PersistenceState s_PendingSnapshot;
        private static bool s_Reconciled;
        private static bool s_ReconcilePending;

        internal static EditorCommandOwnership Load()
        {
            if (s_Ownership != null)
                return s_Ownership;

            var sessionId = SessionState.GetString(SessionIdKey, string.Empty);
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString("N");
                SessionState.SetString(SessionIdKey, sessionId);
            }

            var ownership = new EditorCommandOwnership(sessionId);
            var json = SessionState.GetString(StateKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var persisted = JsonConvert.DeserializeObject<EditorCommandOwnership.PersistenceState>(json);
                    if (persisted == null)
                        ownership.Block("Persisted ownership state is empty; Editor restart is required.");
                    else
                        ownership.Restore(persisted);
                }
                catch (Exception)
                {
                    ownership.Block("Persisted ownership state cannot be read; Editor restart is required.");
                }
            }
            s_Ownership = ownership;
            ownership.Changed += PersistChanged;
            return ownership;
        }

        internal static EditorCommandOwnership GetOrCreate()
        {
            var ownership = Load();
            Reconcile(ownership);
            return ownership;
        }
        internal static bool IsShared(EditorCommandOwnership ownership) =>
            ownership != null && ReferenceEquals(ownership, s_Ownership);

        internal static void Persist(EditorCommandOwnership.PersistenceState state)
        {
            if (state == null)
                return;
            var existing = SessionState.GetString(StateKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
            {
                try
                {
                    var previous = JsonConvert.DeserializeObject<EditorCommandOwnership.PersistenceState>(existing);
                    if (previous != null && previous.EditorSessionId == state.EditorSessionId
                        && previous.Revision > state.Revision)
                        return;
                }
                catch (Exception)
                {
                    // Replace an unreadable old snapshot with the current in-memory authority.
                }
            }
            SessionState.SetString(StateKey, JsonConvert.SerializeObject(state));
        }
        private static void PersistChanged(EditorCommandOwnership.PersistenceState snapshot)
        {
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                Persist(snapshot);
                return;
            }

            var dispatcher = FindDispatcher();
            if (dispatcher == null)
            {
                StorePending(snapshot);
                return;
            }

            try
            {
                dispatcher.Invoke(() => Persist(snapshot), Timeout.Infinite);
            }
            catch (Exception)
            {
                StorePending(snapshot);
            }
        }

        internal static void RegisterServer(EditorPipelineServer server)
        {
            if (server == null || !ReferenceEquals(server.Ownership, s_Ownership))
                return;
            lock (s_ServersGate)
                if (!s_Servers.Contains(server))
                    s_Servers.Add(server);
            PersistCurrent();
        }

        internal static void UnregisterServer(EditorPipelineServer server)
        {
            lock (s_ServersGate)
                s_Servers.Remove(server);
        }

        internal static void PersistCurrent()
        {
            var ownership = s_Ownership;
            if (ownership == null)
                return;
            var snapshot = ownership.SnapshotForPersistence();
            Persist(snapshot);
            lock (s_ServersGate)
                if (s_PendingSnapshot != null
                    && s_PendingSnapshot.EditorSessionId == snapshot.EditorSessionId
                    && s_PendingSnapshot.Revision <= snapshot.Revision)
                    s_PendingSnapshot = null;
        }

        private static Dispatcher FindDispatcher()
        {
            lock (s_ServersGate)
            {
                foreach (var server in s_Servers)
                    if (server.Dispatcher.IsInitialized)
                        return server.Dispatcher;
            }
            return null;
        }

        private static void StorePending(EditorCommandOwnership.PersistenceState snapshot)
        {
            lock (s_ServersGate)
                if (s_PendingSnapshot == null || s_PendingSnapshot.Revision < snapshot.Revision)
                    s_PendingSnapshot = snapshot;
        }

        internal static void Reconcile(EditorCommandOwnership ownership)
        {
            if (ownership == null || !ReferenceEquals(ownership, s_Ownership) || s_Reconciled || s_ReconcilePending)
                return;
            s_ReconcilePending = true;
            EditorApplication.update += ReconcileWhenEditorReady;
        }

        private static void ReconcileWhenEditorReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= ReconcileWhenEditorReady;
            s_ReconcilePending = false;
            ReconcileReady(s_Ownership);
        }

        private static void ReconcileReady(EditorCommandOwnership ownership)
        {
            if (ownership == null || !ReferenceEquals(ownership, s_Ownership) || s_Reconciled)
                return;
            s_Reconciled = true;
            PackageManagerCommand.RecoverInterruptedOperation();
            if (ownership.GetStatus().state == "blocked")
                return;
            var state = ownership.SnapshotForPersistence();
            foreach (var operation in state.Operations ?? new System.Collections.Generic.List<EditorCommandOwnership.Operation>())
            {
                if (IsPackageOperation(operation.Command))
                {
                    if (!PackageManagerCommand.TryReconcileOwnershipActivity(operation.Id, ownership, out var packageReason))
                    {
                        ownership.Block(packageReason ?? $"Package operation '{operation.Command}' has unknown completion.");
                        return;
                    }
                    ownership.ReconcileOperation(operation.Id);
                    continue;
                }
                var compileKnown = operation.Command == "recompile"
                    && RecompileCommand.IsOperationComplete(operation.Id);
                if (!compileKnown && operation.Command == "recompile"
                    && ContainsActivity(state, operation.Id, "compile")
                    && EditorApplication.isCompiling
                    && RecompileCommand.ResumeOwnershipActivity(ownership, operation.Id))
                    compileKnown = true;
                var testCompleted = false;
                var testKnown = operation.Command == "run_tests"
                    && ContainsActivity(state, operation.Id, "test")
                    && PipelineTestRunner.TryReconcileOwnershipActivity(operation.Id, ownership, out testCompleted);
                if (testKnown)
                {
                    ownership.ReconcileOperation(operation.Id);
                    if (testCompleted)
                        ownership.CompleteHostActivity(operation.Id);
                    continue;
                }

                if (compileKnown)
                {
                    ownership.ReconcileOperation(operation.Id);
                    continue;
                }

                ownership.Block($"Editor reloaded while command '{operation.Command}' was active; completion is unknown.");
                return;
            }

            if (ownership.GetStatus().state == "blocked")
                return;

            state = ownership.SnapshotForPersistence();
            foreach (var activity in state.HostActivities ?? new System.Collections.Generic.List<EditorCommandOwnership.HostActivity>())
            {
                if (activity.Kind == "compile")
                {
                    if (RecompileCommand.IsOperationComplete(activity.Id))
                        ownership.CompleteHostActivity(activity.Id);
                    else if (!(EditorApplication.isCompiling
                        && RecompileCommand.ResumeOwnershipActivity(ownership, activity.Id)))
                    {
                        ownership.Block("Editor reloaded with an uncorrelated compile operation; completion is unknown.");
                        return;
                    }
                }
                else if (activity.Kind == "test")
                {
                    if (!PipelineTestRunner.TryReconcileOwnershipActivity(activity.Id, ownership, out var completed))
                    {
                        ownership.Block("Editor reloaded with an uncorrelated test operation; completion is unknown.");
                        return;
                    }
                    if (completed)
                        ownership.CompleteHostActivity(activity.Id);
                }
                else if (activity.Kind == "package")
                {
                    if (!PackageManagerCommand.TryReconcileOwnershipActivity(activity.Id, ownership, out var packageReason))
                    {
                        ownership.Block(packageReason ?? "Editor reloaded with an uncorrelated package operation; completion is unknown.");
                        return;
                    }
                }
                else
                {
                    ownership.Block($"Editor reloaded with unknown host activity '{activity.Kind}'.");
                    return;
                }
            }

            ownership.BlockIfOperationsRemain("Editor reloaded with work whose completion is unknown.");
            ownership.FinalizeRecovery();
        }

        private static bool IsPackageOperation(string command) =>
            command == "package_add" || command == "package_remove" || command == "package_resolve";

        private static bool ContainsActivity(EditorCommandOwnership.PersistenceState state, string id, string kind)
        {
            if (state.HostActivities == null)
                return false;
            foreach (var activity in state.HostActivities)
                if (activity.Id == id && activity.Kind == kind)
                    return true;
            return false;
        }
    }
}

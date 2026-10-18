using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Models;
using Unity.Pipeline.Security;
using UnityEditor;
using UnityEngine;

namespace Unity.Pipeline.Editor
{
    public class EditorPipelineServer : BasePipelineServer
    {
        private readonly EditorCommandOwnership m_Ownership;

        public EditorPipelineServer()
            : this(EditorCommandOwnershipSession.GetOrCreate())
        {
        }

        internal EditorPipelineServer(EditorCommandOwnership ownership)
        {
            m_Ownership = ownership;
        }

        internal EditorCommandOwnership Ownership => m_Ownership;

        protected override object EditorOwnershipProvider => m_Ownership;

        internal void PersistOwnershipForReload()
        {
            if (EditorCommandOwnershipSession.IsShared(m_Ownership))
                EditorCommandOwnershipSession.PersistCurrent();
        }
        private InstanceDescriptor m_InstanceDescriptor;

        /// <summary>
        /// Guards mutate-then-write of m_InstanceDescriptor: /api/status and /api/editor_status
        /// both reach UpdateHeartBeat concurrently now, and InstanceDescriptor.WriteToProjectRoot's
        /// own lock only covers the write, not the field mutations feeding it.
        /// </summary>
        private readonly object m_HeartbeatLock = new object();

        /// <summary>
        /// One-way settle latch (AUTHAPI-35). On a cold project import the server starts (and
        /// writes its descriptor) while the Editor is still importing assets / compiling scripts,
        /// so early main-thread commands land in a not-quite-ready Editor and fail opaquely. The
        /// latch stays false until the Editor is first seen idle after startup, and is scoped to
        /// the EDITOR SESSION via <see cref="SettledSessionKey"/>: once any server instance has
        /// seen the Editor idle, every later instance (domain-reload restart, test server) starts
        /// settled — a server coming up mid-session while a compile/import happens to be in
        /// flight (script-creating test fixtures, recompile loops) must NOT re-arm the gate.
        /// Volatile: set on the main thread, read from HTTP request threads.
        /// </summary>
        private volatile bool m_Settled;
        private bool m_SettleTickSubscribed;

        /// <summary>
        /// SessionState marker: the Editor has been seen idle at least once this session, i.e.
        /// the post-startup settle window is over for the whole session. SessionState survives
        /// domain reloads and dies with the Editor process — exactly the settle window's scope.
        /// Read/written on the main thread only (Start and the update tick); request threads
        /// only read the volatile <see cref="m_Settled"/> snapshot.
        /// </summary>
        private const string SettledSessionKey = "Unity.Pipeline.EditorSessionSettled";

        /// <summary>
        /// Commands that must stay servable while the Editor is settling: editor_status is the
        /// status surface callers poll to observe the busy/settling state itself.
        /// Background (MainThreadRequired = false) commands are always exempt and don't need
        /// listing here.
        /// </summary>
        private static readonly HashSet<string> CommandsServiceableWhileSettling = new HashSet<string> { "editor_status" };
        private static readonly HashSet<string> OwnershipSettlementStatusCommands =
            new HashSet<string> { "editor_status", "recompile_status", "test_status", "package_status" };

        /// <summary>When true, every command transaction is written to the project transaction log.</summary>
        public bool LogRequestsResponses { get; set; }

        public override DateTime StartedAt => m_InstanceDescriptor == null ? new DateTime() : m_InstanceDescriptor.StartedAt;

        /// <summary>
        /// Whether the Editor has been seen idle (not importing, not compiling) at least once
        /// this editor session. Until then the server is "settling": main-thread commands are
        /// rejected with a retryable busy signal instead of executing into a half-ready Editor.
        /// Virtual so tests can pin the unsettled state.
        /// </summary>
        protected virtual bool IsSettled => m_Settled;

        /// <summary>
        /// Whether this Editor session has already completed its settle window (the SessionState
        /// marker any settling server sets on first idle). Main thread only. Exposed protected so
        /// tests can assert it as a precondition before probing the latch's immediate-settle path.
        /// </summary>
        protected static bool IsSessionSettled => SessionState.GetBool(SettledSessionKey, false);

        protected override void ServerStarted()
        {
            var isAutomated = false;
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == "-automated")
                {
                    isAutomated = true;
                    break;
                }
            }
            // Only warn in an interactive Editor: batchmode (CI, upm-pvp, UTR) can't show modal
            // popups, so the warning is pure noise there.
            if (!isAutomated && !Application.isBatchMode)
            {
                Debug.LogWarning("Editor is not in automated mode. Modal Pop up might break continuous command workflow. Start the editor with -automated");
            }

            EditorCommandOwnershipSession.RegisterServer(this);
            InitializeSettleLatch();
        }

        protected override void ServerStopped()
        {
            if (EditorCommandOwnershipSession.IsShared(m_Ownership))
                EditorCommandOwnershipSession.PersistCurrent();
            EditorCommandOwnershipSession.UnregisterServer(this);
            UnsubscribeSettleTick();
        }

        /// <summary>
        /// Arm or immediately release the settle latch. Start() runs on the main thread, so the
        /// Editor state and SessionState are readable directly. A server starts settled when the
        /// session has already settled once (mid-session restarts after domain reloads, test
        /// servers started while a test's own compile/import is in flight) or when the Editor is
        /// idle right now — zero behavior change in both cases. Only a server started before the
        /// session's first idle moment (cold import: [InitializeOnLoad] runs inside the startup
        /// AssetDatabase refresh) arms the gate, and settles on the first update tick that sees
        /// the Editor idle.
        /// </summary>
        private void InitializeSettleLatch()
        {
            if (SessionState.GetBool(SettledSessionKey, false)
                || (!EditorApplication.isCompiling && !EditorApplication.isUpdating))
            {
                MarkSettled();
                return;
            }

            m_SettleTickSubscribed = true;
            EditorApplication.update += SettleTick;
        }

        private void SettleTick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            MarkSettled();
            UnsubscribeSettleTick();
        }

        /// <summary>
        /// Release this instance's latch and mark the whole session settled (main thread only).
        /// </summary>
        private void MarkSettled()
        {
            m_Settled = true;
            SessionState.SetBool(SettledSessionKey, true);
        }

        private void UnsubscribeSettleTick()
        {
            if (!m_SettleTickSubscribed)
                return;

            m_SettleTickSubscribed = false;
            EditorApplication.update -= SettleTick;
        }

        /// <summary>
        /// Reject commands while startup is unsettled, then keep ownership admission closed to new
        /// work during a tracked compile, test, or package operation. Only editor_status,
        /// recompile_status, test_status, and package_status remain serviceable; request-thread reads use the
        /// volatile settle and host-busy snapshots.
        /// </summary>
        protected override string GetBusyReason(CommandInfo command)
        {
            if (!IsSettled && command.MainThreadRequired
                && !CommandsServiceableWhileSettling.Contains(command.Name))
            {
                return "The Editor is still settling after startup (importing assets / compiling scripts), " +
                       "so main-thread commands are not serviceable yet. Retry shortly, or poll /api/status until it reports 'ready'.";
            }

            if (m_Ownership != null && m_Ownership.IsHostBusy
                && !OwnershipSettlementStatusCommands.Contains(command.Name))
            {
                return "A known Editor compile, test, or package operation is still active. Poll recompile_status, test_status, or package_status before starting more work.";
            }

            return null;
        }

        /// <summary>
        /// A domain reload that lands while an async command waits between frames destroys the
        /// command's continuation: its request never answers and its ownership operation stays
        /// unknown until an Editor restart. Compiles still run; their reload waits for the command.
        /// Test runs are exempt: Play Mode and recompile tests reload within a single run.
        /// </summary>
        protected override void HoldReloadsUntil(CommandInfo command, Task pending)
        {
            var mainThread = SynchronizationContext.Current;
            if (mainThread == null || command.Name == "run_tests")
                return;
            EditorApplication.LockReloadAssemblies();
            pending.ContinueWith(_ => mainThread.Post(__ => EditorApplication.UnlockReloadAssemblies(), null),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        protected override void OnTransactionProcessed(string requestJson, string responseJson)
        {
            if (!LogRequestsResponses)
                return;

            PipelineTransactionLog.Append(requestJson, responseJson);
        }

        // Runtime-only commands (eval, reload_file_override, …) are part of the Player surface; don't
        // advertise them when a client is connected to the Editor.
        protected override bool IncludeRuntimeOnlyCommands => false;
        protected override void CreateInstanceDescriptor()
        {
            // Create and write instance descriptor for CLI discovery
            m_InstanceDescriptor = InstanceDescriptor.CreateCurrent(Port);
            InstanceDescriptor.WriteToProjectRoot(m_InstanceDescriptor);
        }

        protected override void DeleteInstanceDescriptor()
        {
            // Clean up instance descriptor file
            if (m_InstanceDescriptor != null)
            {
                InstanceDescriptor.RemoveFromProjectRoot(m_InstanceDescriptor.ProjectPath);
            }
            m_InstanceDescriptor = null;
        }

        protected override void UpdateHeartBeat()
        {
            // Update heartbeat in instance descriptor
            if (m_InstanceDescriptor == null)
                return;

            lock (m_HeartbeatLock)
            {
                // Keep the port file's token current for discovery only — GetToken() validates the
                // live token, so this just catches up to a rotation on the next heartbeat.
                m_InstanceDescriptor.EvalToken = SecurityTokenManager.GetOrCreateToken();
                m_InstanceDescriptor.LastHeartbeat = DateTime.UtcNow;
                try
                {
                    InstanceDescriptor.WriteToProjectRoot(m_InstanceDescriptor);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to update instance descriptor: {ex.Message}");
                }
            }
        }

        protected override object GetServerStatus()
        {
            UpdateHeartBeat();
            // "settling" is checked first: on a cold import the settle window precedes normal
            // operation, and clients waiting for "ready" (instead of firing commands blindly)
            // land after the Editor is actually serviceable (AUTHAPI-35).
            return new
            {
                status = !IsSettled ? "settling" : (m_InstanceDescriptor == null ? "error" : "ready"),
                lastHeartbeat = m_InstanceDescriptor?.LastHeartbeat
            };
        }

        protected override string GetToken()
        {
            // Validate the live token so a rotation/revocation takes effect on the next request (not
            // the next heartbeat). The descriptor's EvalToken is discovery-only.
            return SecurityTokenManager.GetOrCreateToken();
        }
    }
}

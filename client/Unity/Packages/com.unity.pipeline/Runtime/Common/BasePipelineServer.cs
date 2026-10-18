using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Pipeline.Models;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Security;
using Unity.Pipeline.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Pipeline
{
    /// <summary>
    /// HTTP server that enables CLI tools to execute commands in Unity Editor.
    /// Represents an Instance that can serve as a Pipeline Element for automation.
    /// </summary>
    public abstract class BasePipelineServer
    {
        /// <summary>
        /// Maximum accepted request body size (1 MiB). Requests larger than this are rejected with
        /// 413 to bound the memory a single remote request can force the server to buffer.
        /// </summary>
        private const long MaxRequestBodyBytes = 1 * 1024 * 1024;

        /// <summary>
        /// Upper bound on how many bytes we read-and-discard from an over-limit request body before
        /// sending the 413. HttpListener resets the TCP connection if we close the response while the
        /// client is still uploading, and the client then sees a connection error instead of the 413.
        /// Draining lets the client finish so it can read the response. Kept comfortably above
        /// <see cref="MaxRequestBodyBytes"/> to cover clients only slightly over the cap, while still
        /// bounding the work a single abusive request can force (a larger body simply resets).
        /// </summary>
        private const long MaxDrainBytes = 8 * 1024 * 1024;

        /// <summary>
        /// Maximum accepted body size for /api/job/cancel (4 KiB). The body is just an
        /// <c>{"id": "..."}</c> object, so it needs far less headroom than <see cref="MaxRequestBodyBytes"/>.
        /// </summary>
        private const long MaxCancelBodyBytes = 4 * 1024;

        /// <summary>
        /// Dispatcher wait for a detached job's main-thread execution. Nothing is synchronously
        /// waiting on this thread (the job's HTTP response was already sent), so this only needs
        /// to be larger than any command's own timeout (e.g. eval's 24h cap) rather than bounded
        /// by an HTTP-facing default.
        /// </summary>
        private const int UnboundedJobDispatcherTimeoutMs = Timeout.Infinite;

        private bool m_IsRunning;
        private int m_Port;

        /// <summary>
        /// Serializes /api/exec command execution now that requests are processed
        /// concurrently: execs queue exactly as they did when the accept loop was serial,
        /// while read-only endpoints (/api/status, /api/progress, …) answer immediately.
        /// </summary>
        private readonly SemaphoreSlim m_ExecGate = new SemaphoreSlim(1, 1);
        private HttpListener m_HttpListener;
        private readonly Dispatcher m_Dispatcher = new Dispatcher();
        private int m_InFlightRequests;

        /// <summary>This server's own progress-reporting state (see CliProgress).</summary>
        internal CliProgressState Progress { get; } = new CliProgressState();

        /// <summary>This server's own detached-job registry (see PipelineCancellation, /api/job).</summary>
        internal PipelineJobRegistry JobRegistry { get; } = new PipelineJobRegistry();

        /// <summary>
        /// The server instance currently executing a command on this thread, if any. Set only
        /// around the actual command invocation (see <see cref="ExecuteCommandDirect"/>) so
        /// CliProgress.Report/PipelineCancellation — called from arbitrary command code with no
        /// reference to "which server is running me" — resolve to the right instance. Correct
        /// regardless of which physical thread ends up running the command, since the push/read/pop
        /// all happen within one synchronous call frame; a command that awaits and resumes on a
        /// different thread before calling either API would not see it (no command does today).
        /// </summary>
        [ThreadStatic] private static BasePipelineServer m_CurrentServer;
        internal static BasePipelineServer CurrentServer => m_CurrentServer;

        private bool m_WatchdogEnabled;
        private bool m_WatchdogArmed;
        private DateTime m_LastWatchdogCheck;

        /// <summary>
        /// This server's own main-thread dispatcher. Each server instance owns one (no global
        /// singleton), so tests that start their own server never affect any other server's
        /// dispatch. Pump it via ProcessWorkQueue from the main thread (auto-pumped on
        /// EditorApplication.update in the editor; pumped by RuntimePipelineManager.Update in a
        /// player; pumped explicitly by tests).
        /// </summary>
        public Dispatcher Dispatcher => m_Dispatcher;

        /// <summary>
        /// Whether the server is running AND its HTTP listener is actually listening. More accurate
        /// than the internal running flag alone: returns false if the listener was stopped/disposed
        /// (e.g. by a domain reload) even when the flag is stale-true. Cheap and non-blocking.
        ///
        /// A self-HTTP probe was considered for "does it actually respond" but rejected: the server
        /// processes requests strictly one-at-a-time (HandleRequests awaits ProcessRequest), so a
        /// self-probe deadlocks if issued from within a handler and false-negatives whenever a
        /// request is in flight — unsafe for a watchdog. IsListening reliably catches the realistic
        /// failure (listener stopped by a domain reload), which is what we need.
        /// </summary>
        public bool IsRunning => m_IsRunning && m_HttpListener != null && m_HttpListener.IsListening;

        /// <summary>
        /// When enabled, the server periodically checks its own HTTP listener and re-opens it in
        /// place if it died without going through <see cref="Stop"/> (e.g. an unexpected listener
        /// fault). The server instance survives such failures — only the listener dies — so it can
        /// self-heal without an external restart. Default OFF: transient/test servers must not
        /// watchdog. The editor owner enables it for the live server.
        ///
        /// Setting this while the server is running arms/disarms the watchdog immediately; otherwise
        /// it takes effect on the next <see cref="Start"/>. Domain reloads are NOT handled here (the
        /// instance is torn down and recreated by [InitializeOnLoad]); the watchdog only revives a
        /// listener that died while the instance is still alive.
        /// </summary>
        public bool WatchdogEnabled
        {
            get => m_WatchdogEnabled;
            set
            {
                if (m_WatchdogEnabled == value)
                    return;
                m_WatchdogEnabled = value;
                if (!m_IsRunning)
                    return;
                if (value)
                    ArmWatchdog();
                else
                    DisarmWatchdog();
            }
        }

        /// <summary>
        /// How often the watchdog checks the listener (seconds). Default 5.
        /// </summary>
        public double WatchdogIntervalSeconds { get; set; } = 5.0;

        /// <summary>
        /// Port number the server is listening on. 0 if not running.
        /// Range: 7800-7899 for Editor, 7900-7999 for Runtime (avoids unity-tools port range 37800-37899).
        /// </summary>
        public int Port => m_Port;

        public abstract DateTime StartedAt { get; }

        /// <summary>
        /// Whether this server writes/deletes the shared instance descriptor (.unity-pipeline-port).
        /// Test servers override this to false so they never clobber the live server's descriptor —
        /// the test already knows its port, so no discovery file is needed.
        /// </summary>
        protected virtual bool WritesDescriptor => true;

        /// <summary>
        /// Whether this server advertises commands marked [CliCommand(RuntimeOnly = true)] in
        /// its /api/commands listing. Runtime servers list them; Editor servers hide them so a
        /// client connected to an Editor only sees the Editor command surface.
        /// </summary>
        protected virtual bool IncludeRuntimeOnlyCommands => true;
        /// <summary>Optional Editor ownership policy; player servers do not expose this gateway.</summary>
        protected virtual object EditorOwnershipProvider => null;

        private EditorCommandOwnership Ownership => EditorOwnershipProvider as EditorCommandOwnership;


        protected abstract void CreateInstanceDescriptor();
        protected abstract void DeleteInstanceDescriptor();
        protected abstract void UpdateHeartBeat();
        protected abstract object GetServerStatus();
        protected abstract string GetToken();

        protected virtual void ServerStarted()
        {

        }

        /// <summary>
        /// Busy probe for the /api/exec gate: return a non-null, human-readable reason when the
        /// host cannot service <paramref name="command"/> right now (e.g. the Editor is still
        /// importing/compiling while settling after a cold start), or null to let it run. A busy
        /// command is rejected with HTTP 503 and a structured, retryable envelope — before sync
        /// execution and before a detached job is created — instead of executing into a
        /// half-ready host and failing opaquely (AUTHAPI-35). Called on the request thread, so
        /// implementations must only read thread-safe state. Default: never busy.
        /// </summary>
        protected virtual string GetBusyReason(CommandInfo command) => null;

        protected virtual void ServerStopped()
        {

        }

        /// <summary>
        /// This server's current auth token. Exposed to the test assembly (via InternalsVisibleTo)
        /// so the test client can authenticate without re-deriving the token.
        /// </summary>
        internal string Token => GetToken();

        /// <summary>
        /// Start the HTTP server on the specified port or auto-assign from range.
        /// </summary>
        /// <param name="port">Port to bind to, or 0 for auto-assignment from server-specific range</param>
        public void Start(int port = 0)
        {
            if (m_IsRunning)
                return;

            m_Port = port == 0 ? FindAvailablePort() : port;

            try
            {
                // Initialize this server's own dispatcher (captures the main thread; Start() is
                // always called from the main thread).
                m_Dispatcher.Initialize();

                // Warm the token cache on the main thread before the listener accepts requests:
                // per-request auth runs on background threads and the Editor token is SessionState-
                // backed (main-thread-only). Re-runs after every domain reload via [InitializeOnLoad].
                SecurityTokenManager.GetOrCreateToken();

                // Mark running before opening the listener so HandleRequests' loop guard stays true
                // as soon as it starts on the threadpool.
                m_IsRunning = true;
                OpenListener();

                System.Console.WriteLine($"Start HTTP server: port:{m_Port}");

                if (WritesDescriptor)
                    CreateInstanceDescriptor();

                ArmWatchdog();

                // Keep the Editor and player loop (and thus Update -> Dispatcher.ProcessWorkQueue) running
                // while the window is unfocused or minimized this is similar to auto_tick (especially for the Player).
                Application.runInBackground = true;
                ServerStarted();
            }
            catch (Exception)
            {
                m_IsRunning = false;
                m_HttpListener?.Stop();
                m_HttpListener = null;
                throw;
            }
        }

        /// <summary>
        /// Create the HTTP listener, bind it, and start the request-handling loop. Extracted from
        /// <see cref="Start"/> so the watchdog can re-open a dead listener in place without
        /// re-running the rest of startup (dispatcher init, descriptor). Caller sets m_IsRunning.
        /// </summary>
        private void OpenListener()
        {
            m_HttpListener = new HttpListener();
            AddLoopbackPrefixes(m_HttpListener, m_Port);
            m_HttpListener.Start();

            // Start request handling
            _ = Task.Run(HandleRequests);
        }

        /// <summary>
        /// Bind a wildcard-host prefix so the listener accepts any Host header ("127.0.0.1",
        /// "localhost", "::1") no matter how a client — or its DNS resolver — resolves the name.
        /// This is required because Mono's HttpListener binds a hostname prefix to a single resolved
        /// address family and then matches the request's Host <em>literally</em> per prefix: an
        /// explicit "http://127.0.0.1/" prefix rejects "Host: localhost" (and vice-versa) with a
        /// 400. A wildcard sidesteps that. The wildcard binds all interfaces at the socket level, so
        /// access is confined to the local machine by the loopback check in
        /// <see cref="ProcessRequest"/> (plus bearer-token auth).
        /// </summary>
        private static void AddLoopbackPrefixes(HttpListener listener, int port)
        {
            listener.Prefixes.Add($"http://+:{port}/");
        }

        /// <summary>
        /// Stop the HTTP server and clean up resources.
        /// </summary>
        public void Stop()
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;

            DisarmWatchdog();
            System.Console.WriteLine("Pipeline Server stopped");

            try
            {
                if (WritesDescriptor)
                    DeleteInstanceDescriptor();

                m_HttpListener?.Stop();
                m_HttpListener?.Close();

                ServerStopped();
            }
            catch
            {
                // Ignore cleanup errors
            }
            finally
            {
                m_HttpListener = null;
                m_Dispatcher.Shutdown();
            }
            // Note: Keep port value for diagnostic purposes
        }

        /// <summary>
        /// Stop serving before a domain reload, keeping the discovery descriptor for the next
        /// domain to rewrite. The unload waits for thread-pool work running in the old domain, so
        /// a request still waiting there for the main thread hangs the Editor for good (main thread
        /// on a futex, log silent). Close the listener so no request starts, fail queued
        /// main-thread work so those waits return, then give admitted requests a bounded moment to
        /// answer and release their ownership operations before the caller persists ownership.
        /// </summary>
        public void StopForReload(int drainMilliseconds = 2000)
        {
            if (!m_IsRunning)
                return;

            m_IsRunning = false;
            DisarmWatchdog();
            try
            {
                m_HttpListener?.Stop();
                m_HttpListener?.Close();
                ServerStopped();
            }
            catch
            {
                // The domain is going away; nothing to recover here.
            }
            finally
            {
                m_HttpListener = null;
                m_Dispatcher.Shutdown();
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(drainMilliseconds);
            while (Volatile.Read(ref m_InFlightRequests) > 0 && DateTime.UtcNow < deadline)
                Thread.Sleep(5);
        }

        /// <summary>
        /// Arm the watchdog (no-op if disabled or already armed). In the editor the tick rides
        /// EditorApplication.update; in a player it is driven by RuntimePipelineManager.Update via
        /// <see cref="WatchdogTick"/>.
        /// </summary>
        private void ArmWatchdog()
        {
            if (!m_WatchdogEnabled || m_WatchdogArmed)
                return;

            m_WatchdogArmed = true;
            m_LastWatchdogCheck = DateTime.UtcNow;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update += WatchdogTick;
#endif
        }

        private void DisarmWatchdog()
        {
            if (!m_WatchdogArmed)
                return;

            m_WatchdogArmed = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= WatchdogTick;
#endif
        }

        /// <summary>
        /// Watchdog heartbeat: throttled to <see cref="WatchdogIntervalSeconds"/>, re-opens the HTTP
        /// listener in place if it died while the server is still meant to be running. Safe to call
        /// every frame. In the editor it is subscribed to EditorApplication.update by ArmWatchdog;
        /// in a player RuntimePipelineManager.Update calls it.
        /// </summary>
        public void WatchdogTick()
        {
            if (!m_WatchdogArmed)
                return;

#if UNITY_EDITOR
            // Don't fight domain reloads / asset updates — the listener is expected to be torn down
            // then, and [InitializeOnLoad] recreates the server afterwards.
            if (UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                return;
#endif

            var now = DateTime.UtcNow;
            if ((now - m_LastWatchdogCheck).TotalSeconds < WatchdogIntervalSeconds)
                return;
            m_LastWatchdogCheck = now;

            if (m_HttpListener != null && m_HttpListener.IsListening)
                return; // healthy

            try
            {
                // Dispose the dead listener so it releases the port binding before we re-bind a new
                // one on the same port (a stopped-but-not-closed listener can keep the port held).
                try { m_HttpListener?.Close(); } catch { }
                m_HttpListener = null;

                // The instance is alive and the watchdog is armed → the server is meant to be
                // listening. Re-open the listener in place.
                m_IsRunning = true;
                OpenListener();
                Debug.Log($"Pipeline watchdog re-opened HTTP listener on port {m_Port}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pipeline watchdog failed to re-open listener on port {m_Port}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle incoming HTTP requests with routing to appropriate endpoints.
        /// </summary>
        private async Task HandleRequests()
        {
            while (m_IsRunning && m_HttpListener != null && m_HttpListener.IsListening)
            {
                try
                {
                    var context = await m_HttpListener.GetContextAsync();
                    // Process detached so the accept loop keeps serving while a long command
                    // holds its /api/exec connection open. Without this, ANY in-flight exec
                    // blocked every other request — including /api/status probes and the
                    // /api/progress polls that exist precisely for that situation (CLI-488;
                    // the "editor is unresponsive until the command finishes" report in
                    // CLI-335). Command execution itself stays strictly serialized via
                    // m_ExecGate below, so exec ordering semantics are unchanged.
                    _ = ProcessRequestDetached(context);
                }
                catch (ObjectDisposedException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped, exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
            m_IsRunning = false;
        }

        /// <summary>
        /// Run ProcessRequest without the accept loop awaiting it; a per-request failure is
        /// logged and must never tear down the listener loop.
        /// </summary>
        private async Task ProcessRequestDetached(HttpListenerContext context)
        {
            Interlocked.Increment(ref m_InFlightRequests);
            try
            {
                await ProcessRequest(context);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
            finally
            {
                Interlocked.Decrement(ref m_InFlightRequests);
            }
        }

        /// <summary>
        /// Process individual HTTP request and route to appropriate handler.
        /// </summary>
        protected virtual async Task ProcessRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                // Confine access to the local machine. The listener binds a wildcard host (see
                // AddLoopbackPrefixes) so it accepts any Host header, which also means it accepts
                // connections on non-loopback interfaces — reject anything that isn't loopback
                // before doing any other work. Bearer-token auth is still enforced below.
                var remoteAddress = request.RemoteEndPoint?.Address;
                if (remoteAddress == null || !IPAddress.IsLoopback(remoteAddress))
                {
                    await SendStatusResponse(response, 403, "Forbidden", "Only loopback connections are allowed");
                    return;
                }

                // Reject browser-originated requests. Legitimate non-browser clients (CLI, CI) never
                // send an Origin header; emitting no CORS headers and refusing any request that
                // carries one prevents a website in the developer's browser from reaching this
                // local server (and short-circuits CORS preflights).
                if (!string.IsNullOrEmpty(request.Headers["Origin"]))
                {
                    await SendStatusResponse(response, 403, "Forbidden", "Cross-origin requests are not allowed");
                    return;
                }

                // Authenticate every request with the bearer token before routing.
                if (!IsAuthorized(request))
                {
                    await SendStatusResponse(response, 401, "Unauthorized", "Missing or invalid authentication token");
                    return;
                }

                // Route to appropriate endpoint
                var path = request.Url.AbsolutePath.ToLowerInvariant();

                switch (path)
                {
                    case "/api/status":
                        await HandleStatusRequest(response);
                        break;
                    case "/api/editor_status":
                        await HandleEditorStatusRequest(response);
                        break;
                    case "/api/commands":
                        await HandleCommandsRequest(request, response);
                        break;
                    case "/api/exec":
                        if (request.HttpMethod == "POST")
                            await HandleExecRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    case "/api/test-status":
                        await HandleTestStatusRequest(response);
                        break;
                    case "/api/progress":
                        await HandleProgressRequest(response);
                        break;
                    case "/api/job":
                        if (request.HttpMethod == "GET")
                            await HandleJobStatusRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "GET");
                        break;
                    case "/api/job/cancel":
                        if (request.HttpMethod == "POST")
                            await HandleJobCancelRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    case "/api/editor-ownership":
                        if (Ownership == null)
                            await HandleNotFound(response);
                        else if (request.HttpMethod == "GET")
                            await SendJsonResponse(response, 200, Ownership.GetStatus());
                        else
                            await HandleMethodNotAllowed(response, "GET");
                        break;
                    case "/api/editor-ownership/claim":
                        if (Ownership == null)
                            await HandleNotFound(response);
                        else if (request.HttpMethod == "POST")
                            await HandleOwnershipClaimRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    case "/api/editor-ownership/release":
                        if (Ownership == null)
                            await HandleNotFound(response);
                        else if (request.HttpMethod == "POST")
                            await HandleOwnershipReleaseRequest(request, response);
                        else
                            await HandleMethodNotAllowed(response, "POST");
                        break;
                    default:
                        await HandleNotFound(response);
                        break;
                }
            }
            catch (Exception)
            {
                // Ensure response is always closed
                try
                {
                    if (response.OutputStream.CanWrite)
                    {
                        response.StatusCode = 500;
                        response.OutputStream.Close();
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Validate the request's bearer token against this server's token.
        /// </summary>
        private bool IsAuthorized(HttpListenerRequest request)
        {
            var header = request.Headers["Authorization"];
            if (string.IsNullOrEmpty(header))
                return false;

            const string prefix = "Bearer ";
            if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var token = header.Substring(prefix.Length).Trim();
            return SecurityTokenManager.ConstantTimeEquals(token, GetToken());
        }

        /// <summary>
        /// Send a structured JSON error response with an explicit HTTP status code.
        /// </summary>
        private async Task SendStatusResponse(HttpListenerResponse response, int statusCode, string error, string details)
        {
            try
            {
                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var json = JsonConvert.SerializeObject(BaseResponse.Failure(error, details), Formatting.Indented);
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Serialize <paramref name="payload"/> (null fields omitted) and send it as the JSON
        /// body. Shares <see cref="SendStatusResponse"/>'s write guard: a failure while writing
        /// (e.g. the client disconnected mid-response) is swallowed here instead of propagating
        /// into the caller's own catch block, which would otherwise attempt a second response on
        /// the now-dead connection.
        /// </summary>
        private async Task SendJsonResponse(HttpListenerResponse response, int statusCode, object payload)
        {
            try
            {
                var json = JsonConvert.SerializeObject(payload,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                response.StatusCode = statusCode;
                response.ContentType = "application/json";
                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch
            {
                try { response.OutputStream.Close(); } catch { }
            }
        }

        /// <summary>
        /// Handle /api/status endpoint - returns basic server health information.
        /// No Editor API access required, always fast response.
        /// </summary>
        private async Task HandleStatusRequest(HttpListenerResponse response)
        {
            try
            {
                var basicStatus = GetServerStatus();
                var json = JsonConvert.SerializeObject(basicStatus, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Status Error", $"Failed to get status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/editor_status endpoint - returns detailed Editor state via command execution.
        /// Executes the "editor_status" command to get rich Editor information.
        /// </summary>
        private async Task HandleEditorStatusRequest(HttpListenerResponse response)
        {
            try
            {
                // TODO: should this be implemented as a command??

                // Queue behind the same gate as /api/exec so this doesn't run concurrently with
                // an in-flight exec (or another editor_status/test_status call): it dispatches
                // onto the main thread just like exec does, so left ungated it would race rather
                // than queue the way it did under the old serial accept loop.
                object result;
                await m_ExecGate.WaitAsync();
                try
                {
                    result = await ExecuteCommandByName("editor_status", new JObject());
                }
                finally
                {
                    m_ExecGate.Release();
                }

                // The editor_status command returns a StatusResponse directly
                var editorStatus = result as StatusResponse;
                if (editorStatus != null)
                {
                    // Update with server-specific information
                    UpdateStatusWithServerInfo(editorStatus);

                    var json = JsonConvert.SerializeObject(editorStatus, Formatting.Indented);

                    response.StatusCode = 200;
                    response.ContentType = "application/json";

                    var buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentLength64 = buffer.Length;

                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                else
                {
                    Debug.LogError($"HandleEditorStatusRequest: editor_status command returned wrong type: {result?.GetType().FullName ?? "null"}");
                    await SendErrorResponse(response, "Editor Status Error", "editor_status command did not return valid StatusResponse");
                }
            }
            catch (Exception ex)
            {
                var errorMessage = !string.IsNullOrEmpty(ex.Message) ? ex.Message : "[EMPTY EXCEPTION MESSAGE]";
                Debug.LogError($"HandleEditorStatusRequest failed:");
                Debug.LogError($"  Exception Type: {ex.GetType().FullName}");
                Debug.LogError($"  Message: '{errorMessage}'");
                Debug.LogError($"  Full Exception: {ex}");

                await SendErrorResponse(response, "Editor Status Error", $"Failed to get editor status: {errorMessage}");
            }
        }

        /// <summary>
        /// Update StatusResponse with server-specific information.
        /// Used by /api/editor_status to add server metadata to command result.
        /// </summary>
        private void UpdateStatusWithServerInfo(StatusResponse editorStatus)
        {
            UpdateHeartBeat();
            // Ensure heartbeat is current
            editorStatus.LastHeartbeat = DateTime.UtcNow;
        }

        /// <summary>
        /// Handle /api/commands endpoint - returns the available CLI commands.
        ///
        /// Optional query parameters (filters combine with AND):
        ///  - detail: 'full' (default) includes parameters and the generated JSON schema;
        ///    'compact' returns a lightweight index (name, description, tags, package) so a
        ///    client can cheaply browse, then fetch full detail only for the commands it
        ///    intends to invoke.
        ///  - query: case-insensitive substring match on name, description, or any tag.
        ///  - tag: scope to a tag subtree via segment-aware prefix match ('assets' matches
        ///    'assets' and 'assets/import' but not 'assetsx').
        ///  - group_by: 'flat' (default) returns a 'commands' array; 'package' and 'tag'
        ///    return a 'groups' array instead ('tag' is a nested tree mirroring tag/subtag;
        ///    untagged commands land in a node with an empty tag).
        ///  - sort: 'name' (default) or 'package' (originating package, name as tiebreak);
        ///    order: 'asc' (default) or 'desc'. Sorting applies to the flat list before
        ///    pagination and grouping.
        ///  - offset / limit: paginate the filtered, sorted flat list (applied before
        ///    grouping, so pages are deterministic); 'total' reports the match count before
        ///    pagination while 'count' is the number of commands actually returned.
        /// </summary>
        private async Task HandleCommandsRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var queryString = request.QueryString;

                var detail = queryString["detail"] ?? "full";
                bool fullDetail;
                if (string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase))
                    fullDetail = true;
                else if (string.Equals(detail, "compact", StringComparison.OrdinalIgnoreCase))
                    fullDetail = false;
                else
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown detail value '{detail}'. Accepted values: compact, full");
                    return;
                }

                var groupBy = queryString["group_by"] ?? "flat";
                var groupByPackage = string.Equals(groupBy, "package", StringComparison.OrdinalIgnoreCase);
                var groupByTag = string.Equals(groupBy, "tag", StringComparison.OrdinalIgnoreCase);
                if (!groupByPackage && !groupByTag && !string.Equals(groupBy, "flat", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown group_by value '{groupBy}'. Accepted values: flat, package, tag");
                    return;
                }

                var sort = queryString["sort"] ?? "name";
                var sortByPackage = string.Equals(sort, "package", StringComparison.OrdinalIgnoreCase);
                if (!sortByPackage && !string.Equals(sort, "name", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown sort value '{sort}'. Accepted values: name, package");
                    return;
                }

                var order = queryString["order"] ?? "asc";
                var descending = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
                if (!descending && !string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Unknown order value '{order}'. Accepted values: asc, desc");
                    return;
                }

                var offset = 0;
                if (queryString["offset"] != null
                    && (!int.TryParse(queryString["offset"], out offset) || offset < 0))
                {
                    await SendStatusResponse(response, 400, "Invalid Request",
                        $"Invalid offset value '{queryString["offset"]}'. Expected a non-negative integer");
                    return;
                }

                int? limit = null;
                if (queryString["limit"] != null)
                {
                    if (!int.TryParse(queryString["limit"], out var parsedLimit) || parsedLimit < 0)
                    {
                        await SendStatusResponse(response, 400, "Invalid Request",
                            $"Invalid limit value '{queryString["limit"]}'. Expected a non-negative integer");
                        return;
                    }
                    limit = parsedLimit;
                }

                var query = queryString["query"];
                var tag = queryString["tag"];

                var filtered = CommandRegistry.DiscoverCommands()
                    .Where(c => IncludeRuntimeOnlyCommands || !c.RuntimeOnly)
                    .Where(c => MatchesQuery(c, query) && MatchesTagSubtree(c, tag));
                var matching = SortCommands(filtered, sortByPackage, descending).ToList();

                // Paginate the flat, sorted list before any grouping so pages are deterministic.
                IEnumerable<CommandInfo> window = matching.Skip(offset);
                if (limit.HasValue)
                    window = window.Take(limit.Value);
                var page = window.ToList();

                Func<CommandInfo, object> project = c =>
                    fullDetail ? BuildFullCommandResponse(c) : BuildCompactCommandResponse(c);

                var responseData = new Dictionary<string, object>();
                if (groupByPackage)
                    responseData["groups"] = BuildPackageGroups(page, project);
                else if (groupByTag)
                    responseData["groups"] = BuildTagTree(page, project);
                else
                    responseData["commands"] = page.Select(project).ToList();
                responseData["count"] = page.Count;
                responseData["total"] = matching.Count;
                responseData["offset"] = offset;
                responseData["limit"] = limit;
                responseData["server"] = new
                {
                    version = "0.0.1", // TODO: Get from package.json
                    port = m_Port,
                    startTime = StartedAt
                };

                var json = JsonConvert.SerializeObject(responseData, Formatting.Indented);

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/commands request: {ex.Message}");

                // Return error response
                response.StatusCode = 500;
                response.ContentType = "application/json";

                var errorResponse = new { error = "Internal server error", message = ex.Message };
                var errorJson = JsonConvert.SerializeObject(errorResponse);
                var errorBuffer = Encoding.UTF8.GetBytes(errorJson);
                response.ContentLength64 = errorBuffer.Length;

                await response.OutputStream.WriteAsync(errorBuffer, 0, errorBuffer.Length);
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// Build the lightweight index entry for a single command (detail=compact).
        /// </summary>
        private static object BuildCompactCommandResponse(CommandInfo command)
        {
            return new
            {
                name = command.Name,
                description = command.Description,
                tags = command.Tags,
                package = command.Package
            };
        }

        /// <summary>
        /// Build the complete JSON response object for a single command (detail=full).
        /// </summary>
        private object BuildFullCommandResponse(CommandInfo command)
        {
            return new
            {
                name = command.Name,
                description = command.Description,
                tags = command.Tags,
                package = command.Package,
                mainThreadRequired = command.MainThreadRequired,
                runtimeOnly = command.RuntimeOnly,
                parameters = command.Parameters.Select(p => new
                {
                    name = p.Name,
                    description = p.Description,
                    type = p.ParameterType.Name,
                    required = p.Required,
                    defaultValue = p.DefaultValue
                }).ToList(),
                schema = JsonSchemaGenerator.GenerateCommandSchema(command)
            };
        }

        /// <summary>
        /// Order the filtered command list: by name, or by originating package with name as
        /// tiebreak. Both keys follow the requested direction.
        /// </summary>
        private static IEnumerable<CommandInfo> SortCommands(IEnumerable<CommandInfo> commands, bool byPackage, bool descending)
        {
            if (byPackage)
            {
                var byPkg = descending
                    ? commands.OrderByDescending(c => c.Package ?? string.Empty, StringComparer.Ordinal)
                    : commands.OrderBy(c => c.Package ?? string.Empty, StringComparer.Ordinal);
                return descending
                    ? byPkg.ThenByDescending(c => c.Name, StringComparer.Ordinal)
                    : byPkg.ThenBy(c => c.Name, StringComparer.Ordinal);
            }
            return descending
                ? commands.OrderByDescending(c => c.Name, StringComparer.Ordinal)
                : commands.OrderBy(c => c.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// Case-insensitive substring match on the command's name, description, or any tag.
        /// A null/empty query matches everything.
        /// </summary>
        private static bool MatchesQuery(CommandInfo command, string query)
        {
            if (string.IsNullOrEmpty(query))
                return true;
            return command.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || command.Tags.Any(t => t.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Segment-aware tag subtree match: 'assets' matches tags 'assets' and 'assets/import'
        /// but not 'assetsx'. A null/empty tag matches everything.
        /// </summary>
        private static bool MatchesTagSubtree(CommandInfo command, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return true;
            return command.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)
                || t.StartsWith(tag + "/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Group a page of commands by originating package, sorted by package name.
        /// </summary>
        private static List<object> BuildPackageGroups(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            return commands
                .GroupBy(c => c.Package ?? string.Empty)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => (object)new
                {
                    package = g.Key,
                    count = g.Count(),
                    commands = g.Select(project).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// Node of the group_by=tag tree. A command appears in the node of each tag it carries;
        /// untagged commands land in a top-level node with an empty tag.
        /// </summary>
        private class TagTreeNode
        {
            public readonly List<CommandInfo> Commands = new List<CommandInfo>();
            public readonly SortedDictionary<string, TagTreeNode> Children =
                new SortedDictionary<string, TagTreeNode>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Build the nested group_by=tag tree mirroring the tag/subtag hierarchy. Each node's
        /// 'count' covers its whole subtree (a command tagged with several tags under the same
        /// node counts once per tag entry).
        /// </summary>
        private static List<object> BuildTagTree(List<CommandInfo> commands, Func<CommandInfo, object> project)
        {
            var root = new TagTreeNode();
            foreach (var command in commands)
            {
                if (command.Tags.Count == 0)
                {
                    GetOrAddChild(root, string.Empty).Commands.Add(command);
                    continue;
                }
                foreach (var tag in command.Tags)
                {
                    var node = root;
                    foreach (var segment in tag.Split('/'))
                        node = GetOrAddChild(node, segment);
                    node.Commands.Add(command);
                }
            }
            return root.Children
                .Select(kv => ToTagGroup(kv.Key, kv.Value, project))
                .ToList();
        }

        private static TagTreeNode GetOrAddChild(TagTreeNode node, string key)
        {
            if (!node.Children.TryGetValue(key, out var child))
            {
                child = new TagTreeNode();
                node.Children[key] = child;
            }
            return child;
        }

        private static object ToTagGroup(string path, TagTreeNode node, Func<CommandInfo, object> project)
        {
            return new
            {
                tag = path,
                count = SubtreeCommandCount(node),
                commands = node.Commands.Select(project).ToList(),
                children = node.Children
                    .Select(kv => ToTagGroup(path.Length == 0 ? kv.Key : path + "/" + kv.Key, kv.Value, project))
                    .ToList()
            };
        }

        private static int SubtreeCommandCount(TagTreeNode node)
        {
            return node.Commands.Count + node.Children.Values.Sum(SubtreeCommandCount);
        }

        /// <summary>
        /// Handle 404 Not Found responses.
        /// </summary>
        private async Task HandleNotFound(HttpListenerResponse response)
        {
            response.StatusCode = 404;
            response.ContentType = "text/plain";

            var responseText = "Not Found";
            var buffer = Encoding.UTF8.GetBytes(responseText);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Handle 405 Method Not Allowed responses.
        /// </summary>
        private async Task HandleMethodNotAllowed(HttpListenerResponse response, string allowedMethod)
        {
            await SendErrorResponse(response, "Method Not Allowed",
                $"This endpoint only supports {allowedMethod} requests", statusCode: 405);
        }

        /// <summary>
        /// Handle /api/test-status endpoint - returns status of async test execution.
        /// </summary>
        private async Task HandleTestStatusRequest(HttpListenerResponse response)
        {
            try
            {
                // Queue behind the same gate as /api/exec/editor_status — see the comment in
                // HandleEditorStatusRequest. (In practice this rarely blocks: the async-test-status
                // workflow polls this only once run_tests --async_tests has already returned and
                // released the gate.)
                object result;
                await m_ExecGate.WaitAsync();
                try
                {
                    result = await ExecuteCommandByName("test_status", new JObject());
                }
                finally
                {
                    m_ExecGate.Release();
                }

                string jsonResponse;
                if (result is string statusString)
                {
                    // test_status command returns JSON string directly
                    jsonResponse = statusString;
                }
                else
                {
                    // Fallback - serialize whatever was returned
                    jsonResponse = JsonConvert.SerializeObject(result ?? new { status = "no_tests", message = "No test run in progress" });
                }

                response.StatusCode = 200;
                response.ContentType = "application/json";

                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                response.ContentLength64 = buffer.Length;

                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleTestStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Test Status Error", $"Failed to get test status: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> under the exec gate: tracks the in-flight count for
        /// /api/progress ("active" while any exec runs) via CliProgress's own atomic
        /// BeginExecutionCount/EndExecutionCount, owns CliProgress for the duration
        /// (BeginExecution/EndExecution) so a queued execution can't surface the previous one's
        /// stale progress, and resets the explicit report when the last in-flight execution
        /// completes. Shared by the synchronous /api/exec path and <see cref="RunJobDetached"/> so
        /// the two copies of this scaffold can't drift apart.
        /// </summary>
        /// <param name="executionId">Id CliProgress.Report calls during this execution are attributed to.</param>
        /// <param name="body">The gated work to run once the exec gate is acquired.</param>
        /// <param name="preStartCheck">Runs after the gate is acquired but before
        /// CliProgress.BeginExecution; returning false skips <paramref name="body"/> entirely
        /// (used by the job path to honor a cancellation requested while still queued).</param>
        private async Task ExecuteGated(string executionId, Func<Task> body, Func<bool> preStartCheck = null)
        {
            var ownershipContext = EditorCommandOwnershipContext.Current;
            var executionGate = ownershipContext?.Ownership.ExecutionGate ?? m_ExecGate;
            Progress.BeginExecutionCount();
            try
            {
                await executionGate.WaitAsync();
                try
                {
                    if (preStartCheck != null && !preStartCheck())
                        return;

                    Progress.BeginExecution(executionId);
                    try
                    {
                        await body();
                    }
                    finally
                    {
                        Progress.EndExecution(executionId);
                    }
                }
                finally
                {
                    executionGate.Release();
                }
            }
            finally
            {
                Progress.EndExecutionCount();
            }
        }

        /// <summary>
        /// Execute a detached job (CLI-335). A cancellation requested while the job is still
        /// queued prevents it from starting; a running job is only cooperatively cancelable (see
        /// PipelineCancellation).
        /// </summary>
        private async Task RunJobDetached(PipelineJobRecord record, CommandExecutionRequest commandRequest,
            EditorCommandOwnership.OperationContext ownershipContext)
        {
            try
            {
                using (ownershipContext == null ? null : EditorCommandOwnershipContext.Push(ownershipContext))
                {
                    await ExecuteGated(record.Id, async () =>
                    {
                        JobRegistry.MarkRunning(record);
                        try
                        {
                            var result = await ExecuteCommandByName(commandRequest.Command, commandRequest.Parameters,
                                UnboundedJobDispatcherTimeoutMs);
                            if (record.CancellationRequested)
                                JobRegistry.MarkCanceled(record);
                            else
                                JobRegistry.MarkCompleted(record, result);
                        }
                        catch (OperationCanceledException)
                        {
                            JobRegistry.MarkCanceled(record);
                        }
                        catch (Exception ex)
                        {
                            if (record.CancellationRequested)
                                JobRegistry.MarkCanceled(record);
                            else
                                JobRegistry.MarkFailed(record, "Command Execution Failed", ex.Message);
                        }
                    },
                    preStartCheck: () =>
                    {
                        if (record.CancellationRequested)
                        {
                            JobRegistry.MarkCanceled(record);
                            return false;
                        }
                        if (ownershipContext != null
                            && !ownershipContext.Ownership.CanExecute(ownershipContext.Token, ownershipContext.OperationId))
                        {
                            JobRegistry.MarkFailed(record, "Editor Ownership Rejected", "Ownership was revoked before execution began.");
                            return false;
                        }
                        var busyReason = GetBusyReason(ResolveCommand(commandRequest.Command));
                        if (busyReason != null)
                        {
                            JobRegistry.MarkFailed(record, "Editor Busy", busyReason);
                            return false;
                        }
                        return true;
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"RunJobDetached failed: {ex.Message}");
                JobRegistry.MarkFailed(record, "Internal Server Error", ex.Message);
            }
            finally
            {
                ownershipContext?.Ownership.CompleteOperation(ownershipContext.OperationId);
            }
        }

        /// <summary>
        /// Project a progress snapshot to the <c>{title,info,current,total,pct}</c> wire shape
        /// shared by /api/progress and a running job's <c>progress</c> field — kept as one helper
        /// so the two can't drift out of sync with each other or the documented contract.
        /// </summary>
        private static object BuildProgressPayload(CliProgressState.Snapshot snapshot)
        {
            if (!snapshot.HasReport)
                return null;

            return new
            {
                title = snapshot.Title,
                info = snapshot.Info,
                current = snapshot.Current,
                total = snapshot.Total,
                pct = snapshot.Progress01
            };
        }

        /// <summary>Serialize one job record to the wire shape shared by the job endpoints.</summary>
        private object BuildJobResponse(PipelineJobRecord record)
        {
            lock (record.Gate)
            {
                var running = record.State == PipelineJobState.Running;
                var progress = running ? BuildProgressPayload(Progress.Current) : null;

                return new
                {
                    jobId = record.Id,
                    command = record.Command,
                    state = record.State.ToString().ToLowerInvariant(),
                    cancellationRequested = record.CancellationRequested,
                    enqueuedAt = record.EnqueuedAt,
                    startedAt = record.StartedAt,
                    completedAt = record.CompletedAt,
                    result = record.Result,
                    error = record.Error,
                    errorDetails = record.ErrorDetails,
                    progress
                };
            }
        }

        /// <summary>
        /// Handle GET /api/job?id=… — a detached job's state, progress, and (once terminal)
        /// its retained result (CLI-335 poll/reattach). Served on the listener thread.
        /// </summary>
        private async Task HandleJobStatusRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                var id = request.QueryString["id"];
                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Query parameter 'id' is required");
                    return;
                }
                if (!JobRegistry.TryGet(id, out var record))
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}' (jobs do not survive domain reloads and are pruned after retention)");
                    return;
                }

                await SendJsonResponse(response, 200, BuildJobResponse(record));
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobStatusRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Status Error", $"Failed to get job status: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle POST /api/job/cancel — request cancellation of a detached job (CLI-335).
        /// A queued job never starts; a running job gets the cooperative
        /// PipelineCancellation flag (synchronous code cannot be aborted from outside).
        /// </summary>
        private async Task HandleJobCancelRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var ownership = Ownership;
            if (ownership != null
                && !ownership.TryAuthorize(request.Headers["X-Editor-Ownership"], out var ownershipStatus))
            {
                await SendJsonResponse(response, 409, BuildOwnershipConflict(ownershipStatus, "job/cancel"));
                return;
            }
            try
            {
                if (request.ContentLength64 > MaxCancelBodyBytes)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large",
                        $"Request body exceeds the maximum allowed size of {MaxCancelBodyBytes} bytes");
                    return;
                }

                string body;
                try
                {
                    using (var limited = new MaxLengthStream(request.InputStream, MaxCancelBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        body = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendStatusResponse(response, 413, "Payload Too Large", ex.Message);
                    return;
                }
                finally
                {
                    request.InputStream.Dispose();
                }

                string id = null;
                try
                {
                    var parsed = JObject.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                    id = parsed["id"]?.ToString();
                }
                catch (JsonException)
                {
                    // Fall through to the missing-id error below.
                }

                if (string.IsNullOrEmpty(id))
                {
                    await SendStatusResponse(response, 400, "Bad Request", "Request body must be JSON with an 'id' field");
                    return;
                }
                PipelineJobRecord record = null;
                var found = false;
                if (ownership == null)
                {
                    found = JobRegistry.RequestCancel(id, out record);
                }
                else if (!ownership.TryAuthorizeAndRun(request.Headers["X-Editor-Ownership"], () =>
                    {
                        found = JobRegistry.RequestCancel(id, out record);
                    }, out var finalOwnershipStatus))
                {
                    await SendJsonResponse(response, 409, BuildOwnershipConflict(finalOwnershipStatus, "job/cancel"));
                    return;
                }
                if (!found)
                {
                    await SendStatusResponse(response, 404, "Job Not Found", $"No job with id '{id}'");
                    return;
                }

                await SendJsonResponse(response, 200, BuildJobResponse(record));
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleJobCancelRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Job Cancel Error", $"Failed to cancel job: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/progress endpoint — the currently executing command's task progress
        /// (CLI-488): the server side of the CLI's terminal progress bars, the pipeline
        /// equivalent of EditorUtility.DisplayProgressBar.
        ///
        /// Served entirely on the listener thread from CliProgress's lock-protected snapshot —
        /// never marshaled to the main thread — so it stays responsive while a long synchronous
        /// command has the main thread blocked (exactly when progress matters most).
        ///
        /// Contract (all progress fields optional; pct is 0–1):
        /// <code>{"active":true,"progress":{"title":"…","info":"…","current":42,"total":100,"pct":0.42}}</code>
        /// </summary>
        private async Task HandleOwnershipClaimRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            string body;
            try
            {
                using (var limited = new MaxLengthStream(request.InputStream, MaxCancelBodyBytes, leaveOpen: true))
                using (var reader = new StreamReader(limited))
                    body = await reader.ReadToEndAsync();
            }
            catch (RequestTooLargeException ex)
            {
                await SendStatusResponse(response, 413, "Payload Too Large", ex.Message);
                return;
            }
            finally
            {
                request.InputStream.Dispose();
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            }
            catch (JsonException ex)
            {
                await SendStatusResponse(response, 400, "Invalid JSON", ex.Message);
                return;
            }

            var owner = payload["owner"] as JObject;
            var terminalHandle = owner?["terminalHandle"]?.ToString();
            var incarnationId = owner?["incarnationId"]?.ToString();
            var batchId = payload["batchId"]?.ToString();
            if (string.IsNullOrWhiteSpace(terminalHandle)
                || string.IsNullOrWhiteSpace(incarnationId)
                || string.IsNullOrWhiteSpace(batchId))
            {
                await SendStatusResponse(response, 400, "Bad Request",
                    "Request body must contain owner.terminalHandle, owner.incarnationId, and batchId.");
                return;
            }

            if (!Ownership.TryClaim(terminalHandle, incarnationId, batchId, out var token, out var status))
            {
                await SendJsonResponse(response, 409, BuildOwnershipConflict(status, "claim"));
                return;
            }

            await SendJsonResponse(response, 200, new
            {
                success = true,
                token,
                editorSessionId = Ownership.EditorSessionId
            });
        }

        private async Task HandleOwnershipReleaseRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!Ownership.TryRelease(request.Headers["X-Editor-Ownership"], out var status))
            {
                await SendJsonResponse(response, 409, BuildOwnershipConflict(status, "release"));
                return;
            }
            await SendJsonResponse(response, 200, status);
        }

        private static JObject BuildOwnershipConflict(EditorCommandOwnership.Status status, string operation)
        {
            var payload = JObject.FromObject(status);
            payload["success"] = false;
            payload["operation"] = operation;
            if (string.IsNullOrWhiteSpace(status.reason))
                payload["reason"] = $"Editor ownership does not permit {operation} while state is '{status.state}'.";
            return payload;
        }

        private async Task HandleProgressRequest(HttpListenerResponse response)
        {
            try
            {
                var active = Progress.IsActive;
                var progress = active ? BuildProgressPayload(Progress.Current) : null;

                await SendJsonResponse(response, 200, new { active, progress });
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleProgressRequest failed: {ex.Message}");
                await SendErrorResponse(response, "Progress Error", $"Failed to get progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle /api/exec endpoint - execute CLI commands with parameters.
        /// </summary>
        private async Task HandleExecRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            var ownership = Ownership;
            var ownershipToken = request.Headers["X-Editor-Ownership"];
            EditorCommandOwnership.OperationContext ownershipContext = null;
            var handedOffToJob = false;
            if (ownership != null
                && !ownership.TryAuthorizeAdmission(ownershipToken, out var initialOwnershipStatus))
            {
                await SendJsonResponse(response, 409, BuildOwnershipConflict(initialOwnershipStatus, "exec"));
                return;
            }
            var cmd = "";
            string requestBody = null;
            try
            {
                // Reject oversized bodies up front via Content-Length (cheap, before reading anything).
                if (request.ContentLength64 > MaxRequestBodyBytes)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large",
                            $"Request body exceeds the maximum allowed size of {MaxRequestBodyBytes} bytes"), null);
                    return;
                }

                // Read request body, enforcing the same cap while reading in case Content-Length is
                // absent or untruthful (e.g. chunked transfer-encoding).
                try
                {
                    // leaveOpen: on an over-limit read we still need the raw InputStream open to drain
                    // the unsent remainder before responding, so it must outlive the reader/wrapper.
                    using (var limited = new MaxLengthStream(request.InputStream, MaxRequestBodyBytes, leaveOpen: true))
                    using (var reader = new StreamReader(limited))
                    {
                        requestBody = await reader.ReadToEndAsync();
                    }
                }
                catch (RequestTooLargeException ex)
                {
                    // Drain the body first so the client can read the 413 (see DrainRequestBody).
                    await DrainRequestBody(request, MaxDrainBytes);
                    await SendExecResponse(response, 413,
                        BaseResponse.Failure("Payload Too Large", ex.Message), null);
                    return;
                }
                finally
                {
                    // MaxLengthStream left the InputStream open; dispose it now that any read/drain is done.
                    request.InputStream.Dispose();
                }

                if (string.IsNullOrEmpty(requestBody))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Bad Request", "Request body is required"), requestBody);
                    return;
                }

                // Parse command request
                CommandExecutionRequest commandRequest;
                try
                {
                    commandRequest = JsonConvert.DeserializeObject<CommandExecutionRequest>(requestBody);
                }
                catch (JsonException ex)
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid JSON", $"Failed to parse request body: {ex.Message}"), requestBody);
                    return;
                }

                // Validate request structure
                var requestValidationError = commandRequest?.Validate();
                if (!string.IsNullOrEmpty(requestValidationError))
                {
                    await SendExecResponse(response, 400,
                        BaseResponse.Failure("Invalid Request", requestValidationError), requestBody);
                    return;
                }

                cmd = commandRequest.Command;

                // Resolve the command once — the busy gate and the execution path share this
                // single registry lookup instead of scanning twice per request. An unknown name
                // throws here and surfaces as the usual Command Not Found via the catch below
                // (for a "job": true submission that now happens BEFORE a job is created, so a
                // misnamed command fails fast instead of producing a job that fails later).
                var command = ResolveCommand(cmd);

                // Busy gate (AUTHAPI-35): while the host is settling after startup, reject
                // not-yet-serviceable commands up front — before sync execution AND before a
                // detached job is created (a queued job would otherwise run into the half-ready
                // Editor in the background). 503 with a retryable envelope, distinguishable from
                // a genuine command failure. Only the exec endpoint gates; the status endpoints
                // keep working so the busy state itself stays observable.
                var busyReason = GetBusyReason(command);
                if (busyReason != null)
                {
                    await SendExecResponse(response, 503,
                        CommandExecutionResponse.CmdBusy(cmd, busyReason), requestBody);
                    return;
                }
                if (ownership != null
                    && !ownership.TryBeginOperation(ownershipToken, Guid.NewGuid().ToString("N"), cmd,
                        out ownershipContext, out var admissionStatus))
                {
                    await SendJsonResponse(response, 409, BuildOwnershipConflict(admissionStatus, "exec"));
                    return;
                }

                // Detached job (CLI-335): reply with a job id immediately and run the command
                // in the background — the client polls GET /api/job?id=… to reattach and
                // collect the result after its own HTTP timeout would have expired.
                if (commandRequest.Job)
                {
                    if (!JobRegistry.TryCreate(commandRequest.Command, out var jobRecord))
                    {
                        await SendExecResponse(response, 429,
                            CommandExecutionResponse.CmdFailure(commandRequest.Command, "Too Many Queued Jobs",
                                "Too many jobs are queued or running; wait for some to finish and retry."), requestBody);
                        return;
                    }
                    _ = RunJobDetached(jobRecord, commandRequest, ownershipContext);
                    handedOffToJob = ownershipContext != null;
                    // Standard exec envelope; the job handle is the command's "result".
                    await SendExecResponse(response, 200,
                        CommandExecutionResponse.CmdSuccess(commandRequest.Command,
                            new { jobId = jobRecord.Id, state = "queued" }), requestBody);
                    return;
                }

                // Execute command using shared execution logic (see ExecuteGated: one command at
                // a time, exactly as when the accept loop was serial).
                object result = null;
                var operationId = ownershipContext?.OperationId ?? Guid.NewGuid().ToString("N");
                var executionDenied = false;
                string executionBusyReason = null;
                using (ownershipContext == null ? null : EditorCommandOwnershipContext.Push(ownershipContext))
                {
                    await ExecuteGated(operationId, async () =>
                    {
                        result = await ExecuteCommandByName(command, commandRequest.Parameters,
                            commandRequest.Timeout ?? 60000);
                    },
                    preStartCheck: () =>
                    {
                        if (ownershipContext != null
                            && !ownershipContext.Ownership.CanExecute(ownershipContext.Token, ownershipContext.OperationId))
                        {
                            executionDenied = true;
                            return false;
                        }
                        executionBusyReason = GetBusyReason(command);
                        return executionBusyReason == null;
                    });
                }

                if (executionDenied)
                {
                    await SendJsonResponse(response, 409,
                        BuildOwnershipConflict(ownership.GetStatus(), "exec"));
                    return;
                }
                if (executionBusyReason != null)
                {
                    await SendExecResponse(response, 503,
                        CommandExecutionResponse.CmdBusy(cmd, executionBusyReason), requestBody);
                    return;
                }

                // Send success response
                await SendExecResponse(response, 200,
                    CommandExecutionResponse.CmdSuccess(commandRequest.Command, result), requestBody);
            }
            catch (OwnershipExecutionException ex)
            {
                await SendJsonResponse(response, 409, BuildOwnershipConflict(ex.Status, "exec"));
            }
            catch (HostBusyExecutionException ex)
            {
                await SendExecResponse(response, 503, CommandExecutionResponse.CmdBusy(cmd, ex.Message), requestBody);
            }
            catch (ArgumentException ex)
            {
                // Parameter validation errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Parameter Validation Failed", ex.Message), requestBody);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("No command named"))
            {
                // Command not found errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Command Not Found", ex.Message), requestBody);
            }
            catch (InvalidOperationException ex)
            {
                // Command execution errors
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Command Execution Failed", ex.Message), requestBody);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to handle /api/exec request: {ex.Message}");
                await SendExecResponse(response, 400,
                    CommandExecutionResponse.CmdFailure(cmd, "Internal Server Error", ex.Message), requestBody);
            }
            finally
            {
                if (ownershipContext != null && !handedOffToJob)
                    ownershipContext.Ownership.CompleteOperation(ownershipContext.OperationId);
            }
        }

        /// <summary>
        /// Read and discard the remaining request body, up to <paramref name="maxDrainBytes"/> bytes.
        /// When we reject a request early (e.g. 413) the client may still be uploading; if we close the
        /// response while unread data is in flight, HttpListener resets the TCP connection and the client
        /// sees a connection error instead of our HTTP response. Draining lets the upload complete so the
        /// response is delivered. Best-effort and bounded: a body larger than the budget stops draining
        /// (and may reset), which is acceptable for an abusive request.
        /// </summary>
        private static async Task DrainRequestBody(HttpListenerRequest request, long maxDrainBytes)
        {
            try
            {
                var input = request.InputStream;
                var buffer = new byte[16 * 1024];
                long drained = 0;
                while (drained < maxDrainBytes)
                {
                    var read = await input.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;
                    drained += read;
                }
            }
            catch
            {
                // Best-effort: if the client already closed the connection or the stream is gone,
                // there is nothing left to drain and the response send below will handle the rest.
            }
        }

        /// <summary>
        /// Send error response with structured JSON format.
        /// </summary>
        private async Task SendErrorResponse(HttpListenerResponse response, string error, string details = null, int statusCode = 400)
        {
            try
            {
                var errorResponse = BaseResponse.Failure(error, details);
                await SendResponse(response, statusCode, errorResponse);
            }
            catch
            {
                // Ignore errors in error handling
            }
        }

        private async Task SendResponse(HttpListenerResponse response, int statusCode, BaseResponse pipelineResponse)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(pipelineResponse, Formatting.Indented);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        /// <summary>
        /// Send an /api/exec response and notify the transaction hook with the raw request and
        /// response JSON. Single send point for the exec handler so every branch (success and
        /// error) is captured uniformly.
        /// </summary>
        private async Task SendExecResponse(HttpListenerResponse response, int statusCode,
                                            BaseResponse body, string requestJson)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var json = JsonConvert.SerializeObject(body, Formatting.Indented);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            OnTransactionProcessed(requestJson, json);
        }

        /// <summary>
        /// Hook invoked after an /api/exec transaction is sent, with the raw request and response
        /// JSON. No-op by default (the Player never logs); the Editor server overrides this to
        /// write the transaction log.
        /// </summary>
        protected virtual void OnTransactionProcessed(string requestJson, string responseJson) { }

        /// <summary>
        /// Convert a single JSON parameter token to the command's parameter type.
        ///
        /// Agents and the CLI frequently pass structured parameters (e.g. <c>float[]</c> position,
        /// <c>JObject</c> properties) as a JSON-ENCODED STRING — e.g. position <c>"[0,0,0]"</c> or
        /// properties <c>"{\"m_Mass\":0.17}"</c>. Newtonsoft's <c>JValue(string).ToObject(float[])</c>
        /// (and likewise for <c>JObject</c>) returns null, so the parameter silently drops out:
        /// set_transform applies nothing and set_component_properties fails Required-parameter
        /// validation (CLI-219 / CLI-220). To fix this generally — without special-casing any command —
        /// when the token is a string but the target type is NOT string AND the trimmed string starts
        /// with '{' or '[', we re-parse it as a JSON document before converting.
        ///
        /// The '{'/'[' guard is deliberate and narrow: ordinary string params (and ObjectRef string
        /// handles like "/Player", "Assets/Foo.prefab", "guid:..." — none of which start with '{'/'[')
        /// fall straight through to <c>token.ToObject</c>, so the class-level
        /// <see cref="Unity.Pipeline.Models.ObjectRefConverter"/> and plain-string parameters keep
        /// working unchanged. A re-parse that fails falls through to the normal conversion path (the
        /// caller's try/catch then handles any remaining failure).
        /// </summary>
        private static object ConvertParameterToken(Newtonsoft.Json.Linq.JToken token, System.Type targetType)
        {
            if (token == null)
                return null;

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.String
                && targetType != typeof(string)
                && !targetType.IsPrimitive
                && !targetType.IsEnum)
            {
                var s = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var t = s.TrimStart();
                    if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
                    {
                        try { return Newtonsoft.Json.Linq.JToken.Parse(s).ToObject(targetType); }
                        catch (Newtonsoft.Json.JsonException) { /* fall through to the direct conversion */ }
                    }
                }
            }

            return token.ToObject(targetType);
        }

        /// <summary>
        /// Extract command parameters from JSON request and convert to appropriate types.
        /// </summary>
        private object[] ExtractCommandParameters(CommandInfo command, Newtonsoft.Json.Linq.JObject parametersJson)
        {
            var parameterValues = new object[command.Parameters.Count];

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                var paramName = paramInfo.Name;

                // Try to get value from JSON parameters
                object jsonValue = null;
                if (parametersJson != null && parametersJson.ContainsKey(paramName))
                {
                    try
                    {
                        jsonValue = ConvertParameterToken(parametersJson[paramName], paramInfo.ParameterType);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to convert parameter '{paramName}' to {paramInfo.ParameterType.Name}: {ex.Message}");
                        // Conversion failed, will use default value
                    }
                }

                // Use provided value or default value
                if (jsonValue != null)
                {
                    parameterValues[i] = jsonValue;
                }
                else if (paramInfo.DefaultValue != null)
                {
                    parameterValues[i] = paramInfo.DefaultValue;
                }
                else
                {
                    // Use type's default value for value types, null for reference types
                    parameterValues[i] = paramInfo.ParameterType.IsValueType
                        ? Activator.CreateInstance(paramInfo.ParameterType)
                        : null;
                }
            }

            return parameterValues;
        }

        /// <summary>
        /// Validate that all required command parameters are provided.
        /// </summary>
        private string ValidateCommandParameters(CommandInfo command, object[] parameters)
        {
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                var paramInfo = command.Parameters[i];
                var value = parameters[i];

                if (paramInfo.Required && (value == null ||
                    (value is string str && string.IsNullOrEmpty(str))))
                {
                    return $"Required parameter '{paramInfo.Name}' is missing or empty";
                }
            }

            return null; // No validation errors
        }

        /// <summary>
        /// Resolve a command name to its <see cref="CommandInfo"/>, or throw the standard
        /// Command Not Found error. Single registry scan — /api/exec resolves once and shares
        /// the result between the busy gate and execution.
        /// </summary>
        private static CommandInfo ResolveCommand(string commandName)
        {
            var commands = CommandRegistry.DiscoverCommands().ToList();
            var command = commands.FirstOrDefault(c => c.Name == commandName);
            if (command == null)
            {
                var availableCommands = string.Join(", ", commands.Select(c => c.Name));
                var errorMessage = $"No command named '{commandName}' is available. Available: [{availableCommands}]";
                Debug.LogError($"ExecuteCommandByName: {errorMessage}");
                throw new InvalidOperationException(errorMessage);
            }
            return command;
        }

        /// <summary>
        /// Execute a command by name with JSON parameters.
        /// Handles command lookup, parameter validation, and execution.
        /// Used by callers that only hold a name (status endpoints, detached job runner); the
        /// /api/exec handler resolves the command itself and uses the CommandInfo overload.
        /// </summary>
        private async Task<object> ExecuteCommandByName(string commandName, JObject parametersJson, int dispatcherTimeoutMs = 60000)
        {
            return await ExecuteCommandByName(ResolveCommand(commandName), parametersJson, dispatcherTimeoutMs);
        }

        /// <summary>
        /// Execute a pre-resolved command with JSON parameters (parameter extraction, required
        /// validation, threading).
        /// </summary>
        private async Task<object> ExecuteCommandByName(CommandInfo command, JObject parametersJson, int dispatcherTimeoutMs = 60000)
        {
            // Extract parameters
            var parameters = ExtractCommandParameters(command, parametersJson);

            // Validate required parameters
            var validationError = ValidateCommandParameters(command, parameters);
            if (!string.IsNullOrEmpty(validationError))
            {
                Debug.LogError($"ExecuteCommandByName: Parameter validation failed: {validationError}");
                throw new ArgumentException(validationError);
            }

            // Execute command with appropriate threading
            return await ExecuteCommand(command, parameters, dispatcherTimeoutMs);
        }

        /// <summary>
        /// Execute the command method with provided parameters.
        /// Handles main thread execution if required.
        /// </summary>
        private async Task<object> ExecuteCommand(CommandInfo command, object[] parameters, int dispatcherTimeoutMs = 60000)
        {
            var currentContext = EditorCommandOwnershipContext.Current;
            var ownershipContext = currentContext != null && ReferenceEquals(currentContext.Ownership, Ownership)
                ? currentContext
                : null;
            object raw;
            if (command.MainThreadRequired)
            {
                if (m_Dispatcher.IsMainThread())
                {
                    raw = ExecuteOnMainThread(command, parameters, ownershipContext);
                }
                else
                {
                    var requestedTimeoutMs = ownershipContext == null
                        ? ResolveRequestedTimeoutMs(command, parameters)
                        : null;
                    var waitBudgetMs = ownershipContext != null
                        ? Timeout.Infinite
                        : dispatcherTimeoutMs == UnboundedJobDispatcherTimeoutMs
                            ? dispatcherTimeoutMs
                            : requestedTimeoutMs ?? dispatcherTimeoutMs;
                    raw = await Task.Run(() => m_Dispatcher.Invoke(
                        () => ExecuteOnMainThread(command, parameters, ownershipContext), waitBudgetMs));
                }
            }
            else
            {
                raw = await Task.Run(() => ExecuteCommandInContext(command, parameters, ownershipContext));
            }

            return await UnwrapResult(raw);
        }

        private object ExecuteCommandInContext(CommandInfo command, object[] parameters,
            EditorCommandOwnership.OperationContext context)
        {
            using (context == null ? null : EditorCommandOwnershipContext.Push(context))
                return ExecuteCommandDirect(command, parameters);
        }

        /// <summary>
        /// Main thread. A command that returns a still-running Task keeps working across frames;
        /// let the host hold domain reloads until it completes (see <see cref="HoldReloadsUntil"/>).
        /// </summary>
        private object ExecuteOnMainThread(CommandInfo command, object[] parameters,
            EditorCommandOwnership.OperationContext context)
        {
            var raw = ExecuteCommandInContext(command, parameters, context);
            if (raw is Task pending && !pending.IsCompleted)
                HoldReloadsUntil(command, pending);
            return raw;
        }

        /// <summary>
        /// Main thread, called with a command's still-running Task. Default: nothing (a player
        /// never reloads its domain). The Editor server defers reloads until the Task completes.
        /// </summary>
        protected virtual void HoldReloadsUntil(CommandInfo command, Task pending)
        {
        }

        /// <summary>
        /// Commands whose own "timeout" parameter is documented in milliseconds and is meant to bound
        /// the whole main-thread call. Other commands happen to also declare a "timeout" parameter
        /// (e.g. run_tests, in seconds) with different semantics, so this can't be a blanket
        /// name/type match — it has to be opted into per command.
        /// </summary>
        private static readonly HashSet<string> CommandsWithMillisecondTimeoutParameter = new HashSet<string> { "eval", "eval_file" };

        /// <summary>
        /// For commands in <see cref="CommandsWithMillisecondTimeoutParameter"/>, return the value the
        /// caller passed for their own "timeout" parameter. Returns null otherwise, in which case the
        /// caller should fall back to Dispatcher.Invoke's own default wait.
        /// </summary>
        private static int? ResolveRequestedTimeoutMs(CommandInfo command, object[] parameters)
        {
            if (!CommandsWithMillisecondTimeoutParameter.Contains(command.Name))
                return null;

            for (int i = 0; i < command.Parameters.Count; i++)
            {
                if (command.Parameters[i].Name == "timeout" && command.Parameters[i].ParameterType == typeof(int))
                {
                    return (int)parameters[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Commands declared as `async Task`/`Task&lt;T&gt;` return their Task from reflection Invoke.
        /// Await it here (on the calling background thread, leaving the main thread free to pump the
        /// dispatcher) so the actual value is serialized rather than the Task itself, and so a faulted
        /// command surfaces its real exception to the request handler instead of being masked when
        /// Newtonsoft reads Task.Result during serialization.
        /// </summary>
        private static async Task<object> UnwrapResult(object result)
        {
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                // Task&lt;T&gt; exposes a Result property; non-generic Task does not.
                var resultProperty = task.GetType().GetProperty("Result");
                return resultProperty?.GetValue(task);
            }

            return result;
        }

        /// <summary>
        /// Direct command execution using reflection. Sets <see cref="CurrentServer"/> for the
        /// duration so CliProgress/PipelineCancellation calls inside the command body resolve to
        /// this server, regardless of which thread this ends up running on.
        /// </summary>
        private object ExecuteCommandDirect(CommandInfo command, object[] parameters)
        {
            var ownershipContext = EditorCommandOwnershipContext.Current;
            if (ownershipContext != null
                && ReferenceEquals(ownershipContext.Ownership, Ownership)
                && !ownershipContext.Ownership.CanExecute(ownershipContext.Token, ownershipContext.OperationId))
                throw new OwnershipExecutionException(ownershipContext.Ownership.GetStatus());
            var busyReason = GetBusyReason(command);
            if (busyReason != null)
                throw new HostBusyExecutionException(busyReason);
            var previousServer = m_CurrentServer;
            m_CurrentServer = this;
            try
            {
                return command.Method.Invoke(null, parameters);
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Reflection wraps the command's own exception in a TargetInvocationException; surface
                // the inner message so callers (and agents) get an actionable error instead of the
                // generic "Exception has been thrown by the target of an invocation."
                var inner = tie.InnerException;
                Debug.LogError($"Command '{command.Name}' failed: {inner.Message}");

                // Preserve validation-oriented exceptions so HandleExecRequest's dedicated
                // catch (ArgumentException) classifies them as "Parameter Validation Failed" rather than
                // collapsing them into "Command Execution Failed". Rethrow the original with its stack.
                if (inner is ArgumentException)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();

                throw new InvalidOperationException($"Command '{command.Name}' failed: {inner.Message}", inner);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Command execution failed: {ex.Message}");
                throw new InvalidOperationException($"Command '{command.Name}' failed: {ex.Message}", ex);
            }
            finally
            {
                m_CurrentServer = previousServer;
            }
        }
        private sealed class OwnershipExecutionException : Exception
        {
            internal OwnershipExecutionException(EditorCommandOwnership.Status status)
                : base("Editor ownership changed before the command started.")
            {
                Status = status;
            }

            internal EditorCommandOwnership.Status Status { get; }
        }
        private sealed class HostBusyExecutionException : Exception
        {
            internal HostBusyExecutionException(string reason) : base(reason)
            {
            }
        }


        /// <summary>
        /// Get the port range for this server type.
        /// Editor servers use 7800-7899, Runtime servers use 7900-7999.
        /// </summary>
        protected virtual (int basePort, int maxPort) GetPortRange()
        {
            return (7800, 7849); // Editor production (test editor servers use 7850-7899)
        }

        /// <summary>
        /// Find an available port in the pipeline server range.
        /// </summary>
        private int FindAvailablePort()
        {
            var (basePort, maxPort) = GetPortRange();

            for (int port = basePort; port <= maxPort; port++)
            {
                if (IsPortAvailable(port))
                {
                    return port;
                }
            }

            throw new InvalidOperationException($"No available ports in range {basePort}-{maxPort}");
        }

        /// <summary>
        /// Check if a specific port is available for binding.
        /// </summary>
        private bool IsPortAvailable(int port)
        {
            try
            {
                using (var listener = new HttpListener())
                {
                    AddLoopbackPrefixes(listener, port);
                    listener.Start();
                    listener.Stop();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}

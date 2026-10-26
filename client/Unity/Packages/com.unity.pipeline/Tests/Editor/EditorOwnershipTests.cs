using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline;
using Unity.Pipeline.Commands;
using Unity.Pipeline.Editor;
using UnityEngine;

namespace Unity.Pipeline.Tests.Editor
{
    public class EditorOwnershipTests
    {
        private static readonly ManualResetEventSlim s_WaitStarted = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim s_ReleaseWait = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim s_MainWaitStarted = new ManualResetEventSlim(false);
        private static readonly ManualResetEventSlim s_MainReleaseWait = new ManualResetEventSlim(false);
        private static int s_MutationCount;

        private TestEditorPipelineServer m_Server;
        private HttpClient m_Client;

        [SetUp]
        public void SetUp()
        {
            CommandRegistry.SetDiscovery(new TypeCacheCommandDiscovery());
            Interlocked.Exchange(ref s_MutationCount, 0);
            s_WaitStarted.Reset();
            s_ReleaseWait.Reset();
            s_MainWaitStarted.Reset();
            s_MainReleaseWait.Reset();

            var ownership = new EditorCommandOwnership(Guid.NewGuid().ToString("N"));
            m_Server = new TestEditorPipelineServer(ownership);
            m_Server.Start();
            m_Client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{m_Server.Port}"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            m_Client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", m_Server.Token);
        }

        [TearDown]
        public void TearDown()
        {
            s_ReleaseWait.Set();
            s_MainReleaseWait.Set();
            m_Client?.Dispose();
            m_Server?.Stop();
        }

        [CliCommand("ownership_test_mutate", "Increment an observable ownership test counter", MainThreadRequired = false)]
        public static int OwnershipTestMutate()
        {
            return Interlocked.Increment(ref s_MutationCount);
        }

        [CliCommand("ownership_test_wait", "Wait for ownership test release", MainThreadRequired = false)]
        public static string OwnershipTestWait()
        {
            s_WaitStarted.Set();
            if (!s_ReleaseWait.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Ownership test did not release the command body.");
            return "finished";
        }

        [CliCommand("ownership_test_main_wait", "Wait on the Editor thread for ownership test release", MainThreadRequired = true)]
        public static string OwnershipTestMainWait()
        {
            s_MainWaitStarted.Set();
            if (!s_MainReleaseWait.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("Ownership test did not release the main-thread command body.");
            return "main-finished";
        }

        [Test]
        public async Task CompetingClaims_AreAtomicAndConflictIncludesPublicOwner()
        {
            var manager = new EditorCommandOwnership("claim-race-session");
            using (var barrier = new Barrier(2))
            {
                string firstToken = null;
                string secondToken = null;
                EditorCommandOwnership.Status firstStatus = null;
                EditorCommandOwnership.Status secondStatus = null;
                var firstClaimed = false;
                var secondClaimed = false;

                var first = Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(3));
                    firstClaimed = manager.TryClaim("terminal-a", "incarnation-a", "batch-a",
                        out firstToken, out firstStatus);
                });
                var second = Task.Run(() =>
                {
                    barrier.SignalAndWait(TimeSpan.FromSeconds(3));
                    secondClaimed = manager.TryClaim("terminal-b", "incarnation-b", "batch-b",
                        out secondToken, out secondStatus);
                });
                await Task.WhenAll(first, second);

                Assert.AreNotEqual(firstClaimed, secondClaimed, "Exactly one simultaneous caller must own the lease.");
                Assert.IsNotEmpty(firstClaimed ? firstToken : secondToken);
                Assert.IsNull(firstClaimed ? secondToken : firstToken, "A rejected claim must not receive a token.");
                Assert.AreEqual("held", manager.GetStatus().state);
                Assert.AreEqual("held", firstStatus.state);
                Assert.AreEqual("held", secondStatus.state);
                Assert.AreEqual(0, manager.GetStatus().activeOperations);
                Assert.IsTrue(manager.GetStatus().settled);
            }
        }

        [Test]
        public void Release_WaitsForCommandAndHostSettlement_AndReloadKeepsOnlyCurrentSessionToken()
        {
            var manager = new EditorCommandOwnership("reload-session");
            Assert.IsTrue(manager.TryClaim("terminal", "incarnation", "batch", out var token, out _));
            Assert.IsTrue(manager.TryBeginOperation(token, "operation-1", "run_tests", out var operation, out _));
            Assert.IsTrue(manager.BeginHostActivity(operation, "test"));

            var persisted = manager.SnapshotForPersistence();
            var reloaded = new EditorCommandOwnership("reload-session");
            reloaded.Restore(persisted);
            Assert.IsTrue(reloaded.TryAuthorizeAdmission(token, out var reloadedStatus));
            Assert.AreEqual("held", reloadedStatus.state);
            Assert.IsFalse(reloadedStatus.settled);

            Assert.IsTrue(reloaded.TryRelease(token, out var releasing));
            Assert.AreEqual("releasing", releasing.state);
            Assert.AreEqual(1, releasing.activeOperations);
            Assert.IsFalse(releasing.settled);
            Assert.IsTrue(reloaded.CanExecute(token, "operation-1"), "Release closes new admission but preserves admitted work.");

            reloaded.CompleteOperation("operation-1");
            Assert.AreEqual("releasing", reloaded.GetStatus().state, "The async test remains owned after its command returns.");
            reloaded.CompleteHostActivity("operation-1");
            Assert.AreEqual("free", reloaded.GetStatus().state);
            Assert.IsTrue(reloaded.GetStatus().settled);

            var restarted = new EditorCommandOwnership("new-editor-process");
            Assert.IsFalse(restarted.TryAuthorize(token, out var restartedStatus));
            Assert.AreEqual("free", restartedStatus.state);
        }

        [Test]
        public void HumanClaim_WhenFree_HoldsLeaseUntilReleased()
        {
            var ownership = new EditorCommandOwnership("human-free-session");
            Assert.IsTrue(ownership.TryClaimForHuman("playtest", out var held));
            Assert.AreEqual("held", held.state);
            Assert.IsTrue(held.settled, "An idle human lease reads as settled so agents wait instead of blocking.");
            Assert.AreEqual("playtest", ownership.HumanLease);
            Assert.AreEqual(EditorCommandOwnership.HumanHandle, ownership.OwnerHandle);
            Assert.IsNull(ownership.TakeHumanGrant(), "A direct claim is not a queued grant.");

            Assert.IsFalse(ownership.TryClaim("agent", "incarnation", "batch", out _, out var conflict));
            Assert.AreEqual("held", conflict.state);
            StringAssert.Contains("playtesting", conflict.reason);

            Assert.IsTrue(ownership.ReleaseHuman());
            Assert.AreEqual("free", ownership.GetStatus().state);
            Assert.IsTrue(ownership.TryClaim("agent", "incarnation", "batch", out _, out _));
        }

        [Test]
        public void HumanWait_IsHandedTheLeaseWhenAgentBatchSettles_AheadOfOtherAgents()
        {
            var ownership = new EditorCommandOwnership("human-wait-session");
            Assert.IsTrue(ownership.TryClaim("agent-a", "incarnation-a", "batch-a", out var token, out _));
            Assert.IsTrue(ownership.TryBeginOperation(token, "operation-1", "capture", out _, out _));

            Assert.IsFalse(ownership.TryClaimForHuman("playtest", out var waiting));
            Assert.AreEqual("held", waiting.state);
            Assert.AreEqual("playtest", ownership.HumanWaiting);
            Assert.IsNull(ownership.HumanLease);
            Assert.IsTrue(ownership.CanExecute(token, "operation-1"), "Queuing the owner never interrupts admitted agent work.");

            Assert.IsTrue(ownership.TryRelease(token, out var releasing));
            Assert.AreEqual("releasing", releasing.state);
            ownership.CompleteOperation("operation-1");

            var status = ownership.GetStatus();
            Assert.AreEqual("held", status.state, "The settled release hands over without a free window.");
            Assert.AreEqual("playtest", ownership.HumanLease);
            Assert.IsNull(ownership.HumanWaiting);
            Assert.IsFalse(ownership.TryAuthorize(token, out _), "The agent token is revoked by the handoff.");
            Assert.IsFalse(ownership.TryClaim("agent-b", "incarnation-b", "batch-b", out _, out _));
            Assert.AreEqual("playtest", ownership.TakeHumanGrant());
            Assert.IsNull(ownership.TakeHumanGrant(), "A grant is taken once.");
        }

        [Test]
        public void HumanWait_Cancelled_LeavesTheEditorFreeForAgents()
        {
            var ownership = new EditorCommandOwnership("human-cancel-session");
            Assert.IsTrue(ownership.TryClaim("agent", "incarnation", "batch", out var token, out _));
            Assert.IsFalse(ownership.TryClaimForHuman("hold", out _));
            ownership.CancelHumanWait();
            Assert.IsTrue(ownership.TryRelease(token, out var released));
            Assert.AreEqual("free", released.state);
            Assert.IsNull(ownership.HumanLease);
        }

        [Test]
        public void HumanWaitAndLease_SurviveReload()
        {
            var ownership = new EditorCommandOwnership("human-reload-session");
            Assert.IsTrue(ownership.TryClaim("agent", "incarnation", "batch", out var token, out _));
            Assert.IsFalse(ownership.TryClaimForHuman("playtest", out _));

            var serialized = JsonConvert.SerializeObject(ownership.SnapshotForPersistence());
            var reloaded = new EditorCommandOwnership("human-reload-session");
            reloaded.Restore(JsonConvert.DeserializeObject<EditorCommandOwnership.PersistenceState>(serialized));
            Assert.AreEqual("playtest", reloaded.HumanWaiting);

            Assert.IsTrue(reloaded.TryRelease(token, out _));
            serialized = JsonConvert.SerializeObject(reloaded.SnapshotForPersistence());
            var again = new EditorCommandOwnership("human-reload-session");
            again.Restore(JsonConvert.DeserializeObject<EditorCommandOwnership.PersistenceState>(serialized));
            Assert.AreEqual("held", again.GetStatus().state);
            Assert.AreEqual("playtest", again.HumanLease);
            Assert.AreEqual("playtest", again.TakeHumanGrant(), "A grant not yet acted on survives the reload.");
        }

        [Test]
        public void HumanClaim_IsRefusedWhileBlocked()
        {
            var ownership = new EditorCommandOwnership("human-blocked-session");
            ownership.Block("test block");
            Assert.IsFalse(ownership.TryClaimForHuman("playtest", out var status));
            Assert.AreEqual("blocked", status.state);
            Assert.IsNull(ownership.HumanWaiting);
        }

        [Test]
        public void HostActivity_PersistsOriginatingCommandAcrossReload()
        {
            var ownership = new EditorCommandOwnership("host-command-session");
            Assert.IsTrue(ownership.TryClaim("terminal", "incarnation", "batch", out var token, out _));
            Assert.IsTrue(ownership.TryBeginOperation(token, "package-operation", "package_add", out var operation, out _));
            Assert.IsTrue(ownership.BeginHostActivity(operation, "package"));

            var serialized = JsonConvert.SerializeObject(ownership.SnapshotForPersistence());
            var persisted = JsonConvert.DeserializeObject<EditorCommandOwnership.PersistenceState>(serialized);
            Assert.AreEqual("package_add", persisted.HostActivities[0].Command);
            var restored = new EditorCommandOwnership("host-command-session");
            restored.Restore(persisted);
            Assert.AreEqual("package_add", restored.SnapshotForPersistence().HostActivities[0].Command);
        }

        [Test]
        public void ManualRecovery_FreesSettledLease_RevokesTokenAndPersistsAcrossReload()
        {
            var manager = new EditorCommandOwnership("manual-recovery-session");
            Assert.IsTrue(manager.TryClaim("lost-gateway", "incarnation", "old-batch", out var oldToken, out _));
            Assert.IsFalse(manager.TryClaim("next-gateway", "next-incarnation", "next-batch", out _, out _));
            var confirmation = manager.GetRecoveryConfirmation();
            Assert.IsTrue(confirmation.HasValue);
            Assert.IsTrue(manager.TryRecoverSettledLease(confirmation.Value, out var recovered));
            Assert.AreEqual("free", recovered.state);
            Assert.IsTrue(recovered.settled);
            Assert.IsNull(recovered.owner);

            var reloaded = new EditorCommandOwnership("manual-recovery-session");
            reloaded.Restore(manager.SnapshotForPersistence());
            Assert.IsFalse(reloaded.TryAuthorizeAdmission(oldToken, out _));
            Assert.IsFalse(reloaded.TryBeginOperation(oldToken, "stale-operation", "mutate", out _, out _));
            Assert.IsFalse(reloaded.TryRelease(oldToken, out _));
            Assert.IsTrue(reloaded.TryClaim("next-gateway", "next-incarnation", "next-batch", out var nextToken, out _));
            Assert.AreNotEqual(oldToken, nextToken);
            int mutations = 0;
            Assert.IsFalse(reloaded.TryAuthorizeAndRun(oldToken, () => mutations++, out _));
            Assert.IsTrue(reloaded.TryAuthorizeAndRun(nextToken, () => mutations++, out _));
            Assert.AreEqual(1, mutations);
        }

        [TestCase("command")]
        [TestCase("host")]
        [TestCase("blocked")]
        public void ManualRecovery_RejectsActiveOrUnknownWork(string busyKind)
        {
            var manager = new EditorCommandOwnership("busy-recovery-session");
            Assert.IsTrue(manager.TryClaim("gateway", "incarnation", "batch", out var token, out _));
            var confirmation = manager.GetRecoveryConfirmation().Value;
            if (busyKind == "blocked")
                manager.Block("Completion is unknown.");
            else
            {
                Assert.IsTrue(manager.TryBeginOperation(token, "operation", "recompile", out var operation, out _));
                if (busyKind == "host")
                {
                    Assert.IsTrue(manager.BeginHostActivity(operation, "compile"));
                    manager.CompleteOperation(operation.OperationId);
                }
            }
            Assert.IsNull(manager.GetRecoveryConfirmation());
            Assert.IsFalse(manager.TryRecoverSettledLease(confirmation, out var rejected));
            Assert.AreEqual(busyKind == "blocked" ? "blocked" : "held", rejected.state);
            Assert.AreEqual(busyKind == "blocked" ? 0 : 1, rejected.activeOperations);
            Assert.IsFalse(rejected.settled);
        }

        [Test]
        public void ManualRecovery_RejectsStaleConfirmationEvenAfterWorkSettles()
        {
            var manager = new EditorCommandOwnership("revision-recovery-session");
            Assert.IsTrue(manager.TryClaim("gateway", "incarnation", "batch", out var token, out _));
            var confirmation = manager.GetRecoveryConfirmation().Value;
            Assert.IsTrue(manager.TryBeginOperation(token, "operation", "mutate", out _, out _));
            manager.CompleteOperation("operation");
            Assert.IsTrue(manager.GetStatus().settled);
            Assert.IsFalse(manager.TryRecoverSettledLease(confirmation, out _));
            Assert.IsTrue(manager.TryAuthorizeAdmission(token, out _));

            var otherSession = new EditorCommandOwnership("other-session");
            Assert.IsTrue(otherSession.TryClaim("gateway", "incarnation", "batch", out _, out _));
            Assert.IsFalse(otherSession.TryRecoverSettledLease(confirmation, out _));

            Assert.IsTrue(manager.TryRelease(token, out _));
            Assert.IsTrue(manager.TryClaim("other-gateway", "other-incarnation", "other-batch", out var nextToken, out _));
            Assert.IsFalse(manager.TryRecoverSettledLease(confirmation, out _));
            Assert.IsTrue(manager.TryAuthorizeAdmission(nextToken, out _));
        }

        [Test]
        public async Task ExecWithoutLease_IsRejectedBeforeMutation_AndStatusNeverLeaksToken()
        {
            var rejected = await PostAsync("/api/exec", new
            {
                command = "ownership_test_mutate",
                parameters = new { }
            });

            Assert.AreEqual((int)HttpStatusCode.Conflict, rejected.StatusCode);
            Assert.AreEqual("exec", rejected.Json["operation"]?.ToString());
            Assert.AreEqual("free", rejected.Json["state"]?.ToString());
            Assert.AreEqual(0, Interlocked.CompareExchange(ref s_MutationCount, 0, 0));

            var token = await ClaimAsync("terminal", "incarnation", "batch");
            var status = await GetOwnershipStatusAsync();
            Assert.IsFalse(status.Raw.Contains(token), "The public status route must not disclose the lease token.");

            var accepted = await PostAsync("/api/exec", new
            {
                command = "ownership_test_mutate",
                parameters = new { }
            }, token);
            Assert.AreEqual((int)HttpStatusCode.OK, accepted.StatusCode);
            Assert.AreEqual(true, accepted.Json["success"]?.Value<bool>());
            Assert.AreEqual(1, accepted.Json["result"]?.Value<int>());

            var released = await PostAsync("/api/editor-ownership/release", new { }, token);
            Assert.AreEqual((int)HttpStatusCode.OK, released.StatusCode);
            Assert.AreEqual("free", released.Json["state"]?.ToString());
        }

        [Test]
        public async Task PackageStatus_IsDispatchableDuringPackageHostActivity()
        {
            var token = await ClaimAsync("terminal", "incarnation", "package-status");
            var ownership = m_Server.Ownership;
            Assert.IsTrue(ownership.TryBeginOperation(token, "async-package-op", "package_add", out var operation, out _));
            Assert.IsTrue(ownership.BeginHostActivity(operation, "package"));
            ownership.CompleteOperation("async-package-op");

            var response = await PostAsync("/api/exec", new
            {
                command = "package_status",
                parameters = new { }
            }, token);

            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode, response.Raw);
            Assert.AreEqual(true, response.Json["success"]?.Value<bool>(), response.Raw);
            Assert.AreEqual("held", ownership.GetStatus().state);
            for (var attempt = 0; attempt < 100 && ownership.GetStatus().activeOperations != 1; attempt++)
                await Task.Delay(10);
            Assert.AreEqual(1, ownership.GetStatus().activeOperations);

            ownership.CompleteHostActivity("async-package-op");
            var released = await PostAsync("/api/editor-ownership/release", new { }, token);
            Assert.AreEqual((int)HttpStatusCode.OK, released.StatusCode, released.Raw);
            Assert.AreEqual("free", released.Json["state"]?.ToString());
        }

        [Test]
        public async Task CallerTimeout_DoesNotReleaseStartedMainThreadInvocation()
        {
            var token = await ClaimAsync("terminal", "incarnation", "main-timeout");
            var requestCancellation = new CancellationTokenSource();
            var execution = PostAsync("/api/exec", new
            {
                command = "ownership_test_main_wait",
                parameters = new { },
                timeout = 1
            }, token, requestCancellation.Token);

            var scenario = Task.Run(async () =>
            {
                try
                {
                    if (!s_MainWaitStarted.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Main-thread command did not start.");

                    requestCancellation.Cancel();
                    var releaseRequest = PostAsync("/api/editor-ownership/release", new { }, token);
                    OwnershipStatus status = null;
                    for (var attempt = 0; attempt < 100; attempt++)
                    {
                        status = await GetOwnershipStatusAsync();
                        if (status.Json["state"]?.ToString() == "releasing")
                            break;
                        await Task.Delay(10);
                    }

                    if (status?.Json["state"]?.ToString() != "releasing")
                        throw new AssertionException("Release did not retain the running main-thread command.");
                    return await releaseRequest;
                }
                finally
                {
                    s_MainReleaseWait.Set();
                }
            });

            var releaseResponse = await scenario;
            Assert.AreEqual((int)HttpStatusCode.OK, releaseResponse.StatusCode);
            Assert.AreEqual("releasing", releaseResponse.Json["state"]?.ToString());
            Assert.AreEqual(1, releaseResponse.Json["activeOperations"]?.Value<int>());
            Assert.AreEqual(false, releaseResponse.Json["settled"]?.Value<bool>());

            var wasCallerTimedOut = false;
            try
            {
                await execution;
            }
            catch (TaskCanceledException)
            {
                wasCallerTimedOut = true;
            }
            Assert.IsTrue(wasCallerTimedOut, "The caller's canceled HTTP wait should not cancel server-side execution.");
            var free = await WaitForOwnershipStateAsync("free");
            Assert.AreEqual(true, free.Json["settled"]?.Value<bool>());
            requestCancellation.Dispose();
        }

        [Test]
        public async Task DetachedCancellation_DoesNotReleaseUntilCommandBodyExits()
        {
            var token = await ClaimAsync("terminal", "incarnation", "detached-cancel");
            var submission = await PostAsync("/api/exec", new
            {
                command = "ownership_test_wait",
                parameters = new { },
                job = true
            }, token);
            Assert.AreEqual((int)HttpStatusCode.OK, submission.StatusCode);
            var jobId = submission.Json["result"]?["jobId"]?.ToString();
            Assert.IsNotEmpty(jobId);
            Assert.IsTrue(await Task.Run(() => s_WaitStarted.Wait(TimeSpan.FromSeconds(5))));
            await WaitForJobStateAsync(jobId, "running");

            var releasing = await PostAsync("/api/editor-ownership/release", new { }, token);
            Assert.AreEqual("releasing", releasing.Json["state"]?.ToString());
            var cancelled = await PostAsync("/api/job/cancel", new { id = jobId }, token);
            Assert.AreEqual((int)HttpStatusCode.OK, cancelled.StatusCode);
            Assert.AreEqual(true, cancelled.Json["cancellationRequested"]?.Value<bool>());

            var whileBodyRuns = await GetOwnershipStatusAsync();
            Assert.AreEqual("releasing", whileBodyRuns.Json["state"]?.ToString());
            Assert.AreEqual(1, whileBodyRuns.Json["activeOperations"]?.Value<int>());
            Assert.AreEqual(false, whileBodyRuns.Json["settled"]?.Value<bool>());

            s_ReleaseWait.Set();
            await WaitForJobStateAsync(jobId, "canceled");
            var free = await WaitForOwnershipStateAsync("free");
            Assert.AreEqual(true, free.Json["settled"]?.Value<bool>());
        }

        private async Task<string> ClaimAsync(string terminal, string incarnation, string batch)
        {
            var response = await PostAsync("/api/editor-ownership/claim", new
            {
                owner = new { terminalHandle = terminal, incarnationId = incarnation },
                batchId = batch
            });
            Assert.AreEqual((int)HttpStatusCode.OK, response.StatusCode, response.Raw);
            Assert.AreEqual(true, response.Json["success"]?.Value<bool>());
            Assert.IsNotEmpty(response.Json["token"]?.ToString());
            return response.Json["token"].ToString();
        }

        private async Task<OwnershipStatus> GetOwnershipStatusAsync()
        {
            var result = await GetAsync("/api/editor-ownership");
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode, result.Raw);
            return new OwnershipStatus { Json = result.Json, Raw = result.Raw };
        }

        private async Task<HttpResult> WaitForOwnershipStateAsync(string state)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var result = await GetAsync("/api/editor-ownership");
                if (result.Json["state"]?.ToString() == state)
                    return result;
                await Task.Delay(20);
            }
            Assert.Fail($"Ownership never reached state '{state}'.");
            return null;
        }

        private async Task WaitForJobStateAsync(string jobId, string state)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var result = await GetAsync($"/api/job?id={Uri.EscapeDataString(jobId)}");
                if (result.Json["state"]?.ToString() == state)
                    return;
                await Task.Delay(20);
            }
            Assert.Fail($"Job '{jobId}' never reached state '{state}'.");
        }

        private Task<HttpResult> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, null, CancellationToken.None);

        private Task<HttpResult> PostAsync(string path, object body, string token = null) =>
            PostAsync(path, body, token, CancellationToken.None);

        private async Task<HttpResult> PostAsync(string path, object body, string token, CancellationToken cancellationToken) =>
            await SendAsync(HttpMethod.Post, path, body, token, cancellationToken);

        private async Task<HttpResult> SendAsync(HttpMethod method, string path, object body, string token,
            CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(method, path);
            if (body != null)
                request.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(token))
                request.Headers.TryAddWithoutValidation("X-Editor-Ownership", token);
            try
            {
                using (var response = await m_Client.SendAsync(request, cancellationToken))
                {
                    var raw = await response.Content.ReadAsStringAsync();
                    return new HttpResult
                    {
                        StatusCode = (int)response.StatusCode,
                        Raw = raw,
                        Json = string.IsNullOrEmpty(raw) ? new JObject() : JObject.Parse(raw)
                    };
                }
            }
            finally
            {
                request.Dispose();
            }
        }

        private sealed class HttpResult
        {
            public int StatusCode;
            public string Raw;
            public JObject Json;
        }

        private sealed class OwnershipStatus
        {
            public JObject Json;
            public string Raw;
        }
    }
}

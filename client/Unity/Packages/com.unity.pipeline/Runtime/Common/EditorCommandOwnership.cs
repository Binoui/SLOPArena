using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.Pipeline.Security;

namespace Unity.Pipeline
{
    /// <summary>Thread-safe, Unity-independent lease policy for one Editor session.</summary>
    internal sealed class EditorCommandOwnership
    {
        /// <summary>Owner handle of the person at the Editor. Agents never present it.</summary>
        internal const string HumanHandle = "human:owner";

        private readonly object m_Gate = new object();
        private readonly Dictionary<string, Operation> m_Operations = new Dictionary<string, Operation>();
        private readonly Dictionary<string, HostActivity> m_HostActivities = new Dictionary<string, HostActivity>();
        private string m_State = "free";
        private Owner m_Owner;
        private string m_BatchId;
        private string m_Token;
        private string m_Reason;
        private long m_Revision;
        private int m_HostBusy;
        private string m_HumanWaiting;
        private string m_HumanGrant;

        internal EditorCommandOwnership(string editorSessionId)
        {
            if (string.IsNullOrWhiteSpace(editorSessionId))
                throw new ArgumentException("An Editor session id is required", nameof(editorSessionId));
            EditorSessionId = editorSessionId;
        }

        internal string EditorSessionId { get; }
        internal event Action<PersistenceState> Changed;
        internal SemaphoreSlim ExecutionGate { get; } = new SemaphoreSlim(1, 1);
        internal bool IsHostBusy => Volatile.Read(ref m_HostBusy) != 0;

        internal Status GetStatus()
        {
            lock (m_Gate)
                return GetStatusLocked();
        }

        internal bool TryClaim(string terminalHandle, string incarnationId, string batchId,
            out string token, out Status status)
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                if (m_State == "free" && m_HumanWaiting != null)
                    HandToHumanLocked();
                if (m_State != "free")
                {
                    token = null;
                    status = GetStatusLocked();
                    return false;
                }

                m_State = "held";
                m_Owner = new Owner { TerminalHandle = terminalHandle, IncarnationId = incarnationId };
                m_BatchId = batchId;
                m_Token = Guid.NewGuid().ToString("N");
                m_Reason = null;
                changed = ChangedLocked();
                token = m_Token;
                status = GetStatusLocked();
            }
            Publish(changed);
            return true;
        }

        /// <summary>The human lease kind (for example "playtest") while the person holds the Editor.</summary>
        internal string HumanLease
        {
            get { lock (m_Gate) return HumanOwnsLocked() ? m_BatchId : null; }
        }

        /// <summary>The human lease kind queued behind the current agent batch, if any.</summary>
        internal string HumanWaiting
        {
            get { lock (m_Gate) return m_HumanWaiting; }
        }

        internal string OwnerHandle
        {
            get { lock (m_Gate) return m_Owner?.TerminalHandle; }
        }

        /// <summary>
        /// In-process claim for the person at the Editor; there is no HTTP route. Free: held at once.
        /// Already human: switches kind. Held by an agent: queued ahead of every agent and handed
        /// over atomically when that batch settles (see <see cref="TakeHumanGrant"/>). Blocked: refused.
        /// </summary>
        internal bool TryClaimForHuman(string kind, out Status status)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("A human lease kind is required", nameof(kind));
            PersistenceState changed = null;
            bool claimed;
            lock (m_Gate)
            {
                if (m_State == "blocked")
                    claimed = false;
                else if (m_State == "free" || HumanOwnsLocked())
                {
                    GrantHumanLocked(kind);
                    m_HumanWaiting = null;
                    changed = ChangedLocked();
                    claimed = true;
                }
                else
                {
                    if (m_HumanWaiting != kind)
                    {
                        m_HumanWaiting = kind;
                        changed = ChangedLocked();
                    }
                    claimed = false;
                }
                status = GetStatusLocked();
            }
            Publish(changed);
            return claimed;
        }

        internal void CancelHumanWait()
        {
            PersistenceState changed = null;
            lock (m_Gate)
            {
                if (m_HumanWaiting != null)
                {
                    m_HumanWaiting = null;
                    changed = ChangedLocked();
                }
            }
            Publish(changed);
        }

        /// <summary>Ends the human lease; agents' claims are admitted again.</summary>
        internal bool ReleaseHuman()
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                if (!HumanOwnsLocked() || m_Operations.Count != 0 || m_HostActivities.Count != 0)
                    return false;
                m_State = "releasing";
                m_HumanGrant = null;
                FinishReleaseLocked();
                changed = ChangedLocked();
            }
            Publish(changed);
            return true;
        }

        /// <summary>The kind handed over from a queued human claim, once; null otherwise.</summary>
        internal string TakeHumanGrant()
        {
            PersistenceState changed = null;
            string grant;
            lock (m_Gate)
            {
                grant = m_HumanGrant;
                if (grant != null)
                {
                    m_HumanGrant = null;
                    changed = ChangedLocked();
                }
            }
            Publish(changed);
            return grant;
        }

        internal bool HasHostActivity(string kind)
        {
            lock (m_Gate)
                return m_HostActivities.Values.Any(activity => activity.Kind == kind);
        }

        internal bool TryBeginOperation(string token, string id, string command,
            out OperationContext context, out Status status)
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                if (m_State != "held" || !TokenMatchesLocked(token))
                {
                    context = null;
                    status = GetStatusLocked();
                    return false;
                }
                if (m_Operations.ContainsKey(id))
                    throw new InvalidOperationException("Operation id is already active");

                var operation = new Operation { Id = id, Command = command };
                m_Operations.Add(id, operation);
                changed = ChangedLocked();
                context = new OperationContext(this, token, id, command);
                status = GetStatusLocked();
            }
            Publish(changed);
            return true;
        }

        internal bool CanExecute(string token, string operationId)
        {
            lock (m_Gate)
                return (m_State == "held" || m_State == "releasing")
                       && TokenMatchesLocked(token)
                       && m_Operations.ContainsKey(operationId);
        }

        internal bool TryRelease(string token, out Status status)
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                if ((m_State != "held" && m_State != "releasing") || !TokenMatchesLocked(token))
                {
                    status = GetStatusLocked();
                    return false;
                }

                if (m_State == "held")
                {
                    m_State = "releasing";
                    m_Reason = "Release requested; waiting for admitted work and host settlement.";
                }
                FinishReleaseLocked();
                changed = ChangedLocked();
                status = GetStatusLocked();
            }
            Publish(changed);
            return true;
        }

        internal RecoveryConfirmation? GetRecoveryConfirmation()
        {
            lock (m_Gate)
            {
                if (m_State != "held" || m_Operations.Count != 0 || m_HostActivities.Count != 0
                    || m_Owner == null || string.IsNullOrEmpty(m_Token))
                    return null;
                return new RecoveryConfirmation(EditorSessionId, m_Revision, m_Owner, m_BatchId);
            }
        }

        internal bool TryRecoverSettledLease(RecoveryConfirmation expected, out Status status)
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                if (m_State != "held" || m_Operations.Count != 0 || m_HostActivities.Count != 0
                    || m_Owner == null || string.IsNullOrEmpty(m_Token)
                    || expected.EditorSessionId != EditorSessionId || expected.Revision != m_Revision
                    || expected.TerminalHandle != m_Owner.TerminalHandle
                    || expected.IncarnationId != m_Owner.IncarnationId || expected.BatchId != m_BatchId)
                {
                    status = GetStatusLocked();
                    return false;
                }
                m_State = "releasing";
                FinishReleaseLocked();
                changed = ChangedLocked();
                status = GetStatusLocked();
            }
            Publish(changed);
            return true;
        }

        internal bool TryAuthorizeAdmission(string token, out Status status)
        {
            lock (m_Gate)
            {
                var allowed = m_State == "held" && TokenMatchesLocked(token);
                status = GetStatusLocked();
                return allowed;
            }
        }

        internal bool TryAuthorize(string token, out Status status)
        {
            lock (m_Gate)
            {
                var allowed = (m_State == "held" || m_State == "releasing") && TokenMatchesLocked(token);
                status = GetStatusLocked();
                return allowed;
            }
        }
        internal bool TryAuthorizeAndRun(string token, Action action, out Status status)
        {
            lock (m_Gate)
            {
                if ((m_State != "held" && m_State != "releasing") || !TokenMatchesLocked(token))
                {
                    status = GetStatusLocked();
                    return false;
                }
                action();
                status = GetStatusLocked();
                return true;
            }
        }

        internal void CompleteOperation(string operationId)
        {
            PersistenceState changed = null;
            lock (m_Gate)
            {
                if (m_Operations.Remove(operationId))
                {
                    FinishReleaseLocked();
                    changed = ChangedLocked();
                }
            }
            Publish(changed);
        }

        internal bool BeginHostActivity(OperationContext context, string kind)
        {
            if (context == null || !ReferenceEquals(context.Ownership, this))
                return false;

            PersistenceState changed;
            lock (m_Gate)
            {
                if ((m_State != "held" && m_State != "releasing")
                    || !TokenMatchesLocked(context.Token)
                    || !m_Operations.ContainsKey(context.OperationId))
                    return false;

                m_HostActivities[context.OperationId] = new HostActivity
                {
                    Id = context.OperationId,
                    Kind = kind,
                    Command = context.Command
                };
                Volatile.Write(ref m_HostBusy, m_HostActivities.Count);
                changed = ChangedLocked();
            }
            Publish(changed);
            return true;
        }

        internal void CompleteHostActivity(string operationId)
        {
            if (string.IsNullOrEmpty(operationId))
                return;
            PersistenceState changed = null;
            lock (m_Gate)
            {
                if (m_HostActivities.Remove(operationId))
                {
                    Volatile.Write(ref m_HostBusy, m_HostActivities.Count);
                    FinishReleaseLocked();
                    changed = ChangedLocked();
                }
            }
            Publish(changed);
        }

        internal void ReconcileOperation(string operationId)
        {
            CompleteOperation(operationId);
        }

        internal void Block(string reason)
        {
            PersistenceState changed;
            lock (m_Gate)
            {
                m_State = "blocked";
                m_Reason = string.IsNullOrWhiteSpace(reason) ? "Work completion is unknown; Editor restart is required." : reason;
                m_HumanWaiting = null;
                m_HumanGrant = null;
                changed = ChangedLocked();
            }
            Publish(changed);
        }

        internal void BlockIfOperationsRemain(string reason)
        {
            PersistenceState changed = null;
            lock (m_Gate)
            {
                if (m_Operations.Count != 0)
                {
                    BlockLocked(reason);
                    changed = ChangedLocked();
                }
            }
            Publish(changed);
        }

        internal void FinalizeRecovery()
        {
            PersistenceState changed = null;
            lock (m_Gate)
            {
                var previous = m_State;
                FinishReleaseLocked();
                if (previous != m_State)
                    changed = ChangedLocked();
            }
            Publish(changed);
        }

        internal PersistenceState SnapshotForPersistence()
        {
            lock (m_Gate)
                return SnapshotLocked();
        }

        internal void Restore(PersistenceState state)
        {
            if (state == null || state.EditorSessionId != EditorSessionId)
                return;

            lock (m_Gate)
            {
                m_State = state.State;
                m_Owner = state.Owner;
                m_BatchId = state.BatchId;
                m_Token = state.Token;
                m_Reason = state.Reason;
                m_Revision = state.Revision;
                m_HumanWaiting = state.HumanWaiting;
                m_HumanGrant = state.HumanGrant;
                m_Operations.Clear();
                foreach (var operation in state.Operations ?? new List<Operation>())
                    if (!string.IsNullOrEmpty(operation.Id))
                        m_Operations[operation.Id] = operation;
                m_HostActivities.Clear();
                foreach (var activity in state.HostActivities ?? new List<HostActivity>())
                    if (!string.IsNullOrEmpty(activity.Id))
                        m_HostActivities[activity.Id] = activity;
                Volatile.Write(ref m_HostBusy, m_HostActivities.Count);

                if (m_State != "free" && m_State != "held" && m_State != "releasing" && m_State != "blocked")
                    BlockLocked("Persisted ownership state is invalid; Editor restart is required.");
                else if ((m_State == "held" || m_State == "releasing") && (m_Owner == null || string.IsNullOrEmpty(m_Token)))
                    BlockLocked("Persisted lease is incomplete; Editor restart is required.");
                else if (m_State == "free"
                    && (m_Owner != null || !string.IsNullOrEmpty(m_Token)
                        || m_Operations.Count != 0 || m_HostActivities.Count != 0))
                    BlockLocked("Persisted free state contains unsettled ownership data; Editor restart is required.");
                else if (m_State == "free" && m_HumanWaiting != null)
                    HandToHumanLocked();
            }
        }

        private bool TokenMatchesLocked(string token)
        {
            return !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(m_Token)
                   && SecurityTokenManager.ConstantTimeEquals(token, m_Token);
        }

        private void FinishReleaseLocked()
        {
            if (m_State != "releasing" || m_Operations.Count != 0 || m_HostActivities.Count != 0)
                return;
            m_State = "free";
            m_Owner = null;
            m_BatchId = null;
            m_Token = null;
            m_Reason = null;
            if (m_HumanWaiting != null)
                HandToHumanLocked();
        }

        private bool HumanOwnsLocked() =>
            (m_State == "held" || m_State == "releasing") && m_Owner?.TerminalHandle == HumanHandle;

        private void HandToHumanLocked()
        {
            GrantHumanLocked(m_HumanWaiting);
            m_HumanGrant = m_HumanWaiting;
            m_HumanWaiting = null;
        }

        private void GrantHumanLocked(string kind)
        {
            m_State = "held";
            m_Owner = new Owner { TerminalHandle = HumanHandle, IncarnationId = EditorSessionId };
            m_BatchId = kind;
            m_Token ??= Guid.NewGuid().ToString("N");
            m_Reason = kind == "playtest"
                ? "The owner is playtesting in the Editor; agent batches wait until they press Stop."
                : $"The owner holds the Editor ({kind}); agent batches wait until they release it.";
        }

        private void BlockLocked(string reason)
        {
            m_State = "blocked";
            m_Reason = reason;
            m_HumanWaiting = null;
            m_HumanGrant = null;
        }

        private Status GetStatusLocked()
        {
            var activeCount = m_Operations.Count;
            foreach (var id in m_HostActivities.Keys)
                if (!m_Operations.ContainsKey(id))
                    activeCount++;
            return new Status
            {
                success = true,
                state = m_State,
                editorSessionId = EditorSessionId,
                owner = m_Owner == null ? null : new
                {
                    terminalHandle = m_Owner.TerminalHandle,
                    incarnationId = m_Owner.IncarnationId,
                    batchId = m_BatchId
                },
                activeOperations = activeCount,
                settled = (m_State == "held" || m_State == "free") && activeCount == 0,
                reason = m_Reason
            };
        }

        private PersistenceState ChangedLocked()
        {
            m_Revision++;
            return SnapshotLocked();
        }

        private PersistenceState SnapshotLocked()
        {
            return new PersistenceState
            {
                EditorSessionId = EditorSessionId,
                State = m_State,
                Owner = m_Owner == null ? null : new Owner
                {
                    TerminalHandle = m_Owner.TerminalHandle,
                    IncarnationId = m_Owner.IncarnationId
                },
                BatchId = m_BatchId,
                Token = m_Token,
                Reason = m_Reason,
                Revision = m_Revision,
                HumanWaiting = m_HumanWaiting,
                HumanGrant = m_HumanGrant,
                Operations = m_Operations.Values.Select(o => new Operation { Id = o.Id, Command = o.Command }).ToList(),
                HostActivities = m_HostActivities.Values.Select(a =>
                    new HostActivity { Id = a.Id, Kind = a.Kind, Command = a.Command }).ToList()
            };
        }

        private void Publish(PersistenceState snapshot)
        {
            if (snapshot != null)
                Changed?.Invoke(snapshot);
        }

        internal sealed class Status
        {
            public bool success;
            public string state;
            public string editorSessionId;
            public object owner;
            public int activeOperations;
            public bool settled;
            public string reason;
        }

        internal readonly struct RecoveryConfirmation
        {
            internal RecoveryConfirmation(string sessionId, long revision, Owner owner, string batchId)
            {
                EditorSessionId = sessionId;
                Revision = revision;
                TerminalHandle = owner.TerminalHandle;
                IncarnationId = owner.IncarnationId;
                BatchId = batchId;
            }

            internal string EditorSessionId { get; }
            internal long Revision { get; }
            internal string TerminalHandle { get; }
            internal string IncarnationId { get; }
            internal string BatchId { get; }
        }

        internal sealed class Owner
        {
            public string TerminalHandle;
            public string IncarnationId;
        }

        internal sealed class Operation
        {
            public string Id;
            public string Command;
        }

        internal sealed class HostActivity
        {
            public string Id;
            public string Kind;
            public string Command;
        }

        internal sealed class PersistenceState
        {
            public string EditorSessionId;
            public string State;
            public Owner Owner;
            public string BatchId;
            public string Token;
            public string Reason;
            public long Revision;
            public string HumanWaiting;
            public string HumanGrant;
            public List<Operation> Operations;
            public List<HostActivity> HostActivities;
        }

        internal sealed class OperationContext
        {
            internal OperationContext(EditorCommandOwnership ownership, string token, string operationId, string command)
            {
                Ownership = ownership;
                Token = token;
                OperationId = operationId;
                Command = command;
            }

            internal EditorCommandOwnership Ownership { get; }
            internal string Token { get; }
            internal string OperationId { get; }
            internal string Command { get; }
        }
    }

    internal static class EditorCommandOwnershipContext
    {
        private static readonly AsyncLocal<EditorCommandOwnership.OperationContext> s_Current =
            new AsyncLocal<EditorCommandOwnership.OperationContext>();

        internal static EditorCommandOwnership.OperationContext Current => s_Current.Value;

        internal static IDisposable Push(EditorCommandOwnership.OperationContext context)
        {
            var previous = s_Current.Value;
            s_Current.Value = context;
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private readonly EditorCommandOwnership.OperationContext m_Previous;
            private bool m_Disposed;

            internal Scope(EditorCommandOwnership.OperationContext previous) => m_Previous = previous;

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                s_Current.Value = m_Previous;
            }
        }
    }
}

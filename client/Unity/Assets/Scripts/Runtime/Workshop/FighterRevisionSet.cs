#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SlopArena.Client.Workshop
{
    public sealed class FighterRevisionSet<T> : IDisposable where T : class, IDisposable
    {
        Dictionary<string, T> current = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, T> Current => current;
        public async Task Replace(Func<IReadOnlyDictionary<string, T>, Dictionary<string, T>, Task> build,
            Action<IReadOnlyDictionary<string, T>> publish, CancellationToken cancel)
        {
            var previous = current;
            var next = new Dictionary<string, T>(StringComparer.Ordinal);
            bool committed = false;
            try
            {
                await build(previous, next);
                cancel.ThrowIfCancellationRequested();
                publish(next);
                current = next;
                committed = true;
            }
            finally
            {
                var abandoned = committed ? previous.Values.Except(next.Values) : next.Values.Except(previous.Values);
                foreach (var item in abandoned.Distinct()) item.Dispose();
            }
        }
        public void Dispose()
        { foreach (var item in current.Values.Distinct()) item.Dispose(); current.Clear(); }
    }

    public sealed class WorkshopDownloadRetry
    {
        readonly Dictionary<ulong, (string Change, int Attempt, double Next)> pending = new();
        public bool Request(ulong id, string change, double now, bool downloading)
        {
            if (!pending.TryGetValue(id, out var state) || state.Change != change) state = (change, 0, 0);
            if (downloading) { pending[id] = state; return false; }
            if (now < state.Next) return false;
            pending[id] = (change, state.Attempt + 1, now + Math.Min(300, 5 * Math.Pow(2, Math.Min(6, state.Attempt))));
            return true;
        }
        public void Complete(ulong id) => pending.Remove(id);
    }
}

using System;

namespace SlopArena.Shared.Rollback
{
    /// <summary>Where the input a peer consumed at one tick came from.</summary>
    public enum ConsumedInputSource : byte
    {
        /// <summary>The peer's own input for the tick arrived in time.</summary>
        Received,
        /// <summary>Missing: the newest known input repeated without press edges.</summary>
        Held,
        /// <summary>Missing for longer than the hold window: no controls.</summary>
        Neutral,
    }

    /// <summary>The input the simulation consumes for one peer at one tick.</summary>
    public readonly struct ConsumedInput
    {
        public ConsumedInput(InputState input, ConsumedInputSource source, int rescuedPresses)
        {
            Input = input;
            Source = source;
            RescuedPresses = rescuedPresses;
        }

        public InputState Input { get; }
        public ConsumedInputSource Source { get; }
        /// <summary>Late inputs whose press edges were merged into this tick.</summary>
        public int RescuedPresses { get; }
    }

    /// <summary>What one datagram's late inputs contributed.</summary>
    public readonly struct LateInputReport
    {
        public LateInputReport(int rescuedPresses, int droppedPresses)
        {
            RescuedPresses = rescuedPresses;
            DroppedPresses = droppedPresses;
        }

        /// <summary>Late inputs whose presses will merge into the next consumed tick.</summary>
        public int RescuedPresses { get; }
        /// <summary>Late inputs whose presses arrived beyond the rescue window.</summary>
        public int DroppedPresses { get; }
    }

    /// <summary>
    /// One peer's input stream on the GameServer. Inputs ahead of the simulation queue by
    /// tick and each simulated tick consumes exactly its own input. A missing tick repeats the
    /// newest known input without press edges for up to <see cref="HoldTicks"/> after that
    /// input's tick, then becomes neutral. Inputs that arrive after their tick ran still
    /// refresh the newest known input, and their press edges merge into the next consumed tick
    /// when they are at most <see cref="RescueTicks"/> late, once per source tick: a short
    /// upload stall delays a press instead of losing it.
    /// </summary>
    public sealed class PeerInputTimeline
    {
        /// <summary>About a third of a second at 60 Hz.</summary>
        public const uint DefaultHoldTicks = 20;
        /// <summary>About a sixth of a second at 60 Hz.</summary>
        public const uint DefaultRescueTicks = 10;

        private readonly TickInputBuffer _queue = new();
        private InputState _latest;
        private uint _latestTick;
        private bool _hasLatest;
        private readonly HandledInput[] _receivedOrRescued;

        public PeerInputTimeline(uint holdTicks = DefaultHoldTicks, uint rescueTicks = DefaultRescueTicks)
        {
            HoldTicks = holdTicks;
            RescueTicks = rescueTicks;
            _receivedOrRescued = new HandledInput[checked((int)rescueTicks + 1)];
        }

        public uint HoldTicks { get; }
        public uint RescueTicks { get; }

        /// <summary>
        /// Accept consecutive inputs ending at <paramref name="newestTick"/> while the simulation
        /// has completed <paramref name="lastSimulatedTick"/>. Inputs more than
        /// <paramref name="horizonTicks"/> ahead are ignored; duplicate ticks replace each other.
        /// </summary>
        public LateInputReport Accept(uint lastSimulatedTick, uint newestTick,
            ReadOnlySpan<InputState> inputs, uint horizonTicks)
        {
            int rescued = 0, dropped = 0;
            for (int i = 0; i < inputs.Length; i++)
            {
                uint tick = InputBundle.TickAt(newestTick, inputs.Length, i);
                if (tick > lastSimulatedTick)
                {
                    if (tick - lastSimulatedTick <= horizonTicks)
                        _queue.Push(tick, inputs[i]);
                    continue;
                }

                // Newer evidence of what the player holds, even though its tick already ran.
                if (!_hasLatest || tick > _latestTick)
                    Remember(tick, inputs[i]);
                if (!inputs[i].HasPressEdges)
                    continue;
                ref var handled = ref _receivedOrRescued[tick % (uint)_receivedOrRescued.Length];
                if (handled.Tick == tick)
                    continue;
                if ((ulong)lastSimulatedTick + 1 - tick > RescueTicks)
                {
                    dropped++;
                    continue;
                }
                handled = new HandledInput { Tick = tick };
                handled.PendingEdges = inputs[i];
                rescued++;
            }
            return new LateInputReport(rescued, dropped);
        }

        /// <summary>The input the simulation consumes at <paramref name="tick"/>.</summary>
        public ConsumedInput Consume(uint tick)
        {
            if (tick > 0)
                _queue.Prune(tick - 1);
            InputState input;
            ConsumedInputSource source;
            if (_queue.TryTake(tick, out var received))
            {
                input = received;
                source = ConsumedInputSource.Received;
                Remember(tick, received);
                _receivedOrRescued[tick % (uint)_receivedOrRescued.Length] = new HandledInput { Tick = tick };
            }
            else if (_hasLatest && tick - _latestTick <= HoldTicks)
            {
                input = _latest.WithoutEdges();
                source = ConsumedInputSource.Held;
            }
            else
            {
                input = default;
                source = ConsumedInputSource.Neutral;
            }

            int rescued = 0;
            InputState pending = default;
            uint newestSlotTick = 0;
            for (int i = 0; i < _receivedOrRescued.Length; i++)
            {
                ref var handled = ref _receivedOrRescued[i];
                if (handled.PendingEdges.HasPressEdges && handled.Tick.HasValue && tick >= handled.Tick.Value
                    && tick - handled.Tick.Value <= RescueTicks)
                {
                    pending = pending.WithEdgesFrom(handled.PendingEdges);
                    if (handled.PendingEdges.ActiveSlot != 0 && handled.Tick.Value >= newestSlotTick)
                    {
                        pending.ActiveSlot = handled.PendingEdges.ActiveSlot;
                        newestSlotTick = handled.Tick.Value;
                    }
                    rescued++;
                }
                handled.PendingEdges = default;
            }
            return new ConsumedInput(input.WithEdgesFrom(pending), source, rescued);
        }

        /// <summary>Forget queued and remembered input (connection change, disconnect, elimination).</summary>
        public void Reset()
        {
            _queue.Clear();
            _latest = default;
            _latestTick = 0;
            _hasLatest = false;
            Array.Clear(_receivedOrRescued, 0, _receivedOrRescued.Length);
        }

        private struct HandledInput
        {
            public uint? Tick;
            public InputState PendingEdges;
        }

        private void Remember(uint tick, InputState input)
        {
            _latest = input;
            _latestTick = tick;
            _hasLatest = true;
        }
    }
}

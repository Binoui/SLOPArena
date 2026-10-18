using System;
using System.Collections.Generic;
using SlopArena.Shared.Rollback;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// In-process netplay simulation: an authoritative ServerSimulation ("the server")
/// and a RollbackSimulator ("the client") wired through the real packet codecs
/// (CharacterStatePacket.FromState → ServerEntityPacket) with configurable packet
/// delay (RTT) and loss. No UDP, no threads — deterministic.
///
/// Per tick, in bridge order:
///   1. server ticks with (in1, in2)                    → authoritative truth
///   2. client predicts self with in1 (RollbackSimulator.Tick)
///   3. delayed server packets are applied              → ReconcileSelf + IngestOpponentBatch
///
/// Both sides consume identical inputs, so with no loss the client converges to the
/// server exactly (the sim is deterministic). The opponent's PredictedTrack has no
/// self hurtbox, so cross-hits make damage/knockback legitimately diverge — hence
/// AssertOpponentConverged uses tolerance.
/// </summary>
internal sealed class NetplayHarness
{
    public const ulong SelfId = 1;
    public const ulong OpponentId = 2;

    private readonly ServerSimulation _server;
    private readonly RollbackSimulator _client;
    private readonly int _delayTicks;
    private readonly int _dropEvery; // 0 = no loss
    private bool _dropsEnabled = true;
    private readonly Queue<(uint Tick, ServerEntityPacket Self, ServerEntityPacket Opp)> _inFlight = new();
    private uint _serverTick;

    public NetplayHarness(ArenaDefinition arena, CharacterDefinition def, int delayTicks = 0, int dropEvery = 0)
    {
        _delayTicks = delayTicks;
        _dropEvery = dropEvery;

        // Both entities spawn grounded on the arena floor; the same initial states
        // are registered on both sides so the trace starts converged.
        var p1 = TestHelpers.PlayerState();
        p1.PY = TestHelpers.GroundPY(def);
        var p2 = TestHelpers.PlayerState(x: 10f);
        p2.PY = TestHelpers.GroundPY(def);

        _server = new ServerSimulation(arena);
        _server.RegisterEntity(SelfId, def, p1);
        _server.RegisterEntity(OpponentId, def, p2);

        _client = new RollbackSimulator(arena, SelfId);
        _client.RegisterEntity(SelfId, def, p1);
        _client.RegisterEntity(OpponentId, def, p2);
    }

    /// <summary>Align both simulations to an authority tick before the first prediction step.</summary>
    public bool SetTimeline(uint lastCompletedTick)
    {
        if (_serverTick != 0 || !_client.SetTimeline(lastCompletedTick))
            return false;
        _serverTick = lastCompletedTick;
        _server.SetTick(lastCompletedTick);
        return true;
    }

    /// <summary>One authority step with independently timed uplink inputs. The legacy overload
    /// keeps client prediction and server consumption identical for existing traces.</summary>
    public void Step(InputState clientSelfInput, InputState serverSelfInput, InputState opponentInput)
    {
        _serverTick++;
        _server.Tick(new Dictionary<ulong, InputState>
        {
            { SelfId, serverSelfInput },
            { OpponentId, opponentInput },
        });

        _client.Tick(new Dictionary<ulong, InputState> { { SelfId, clientSelfInput } });

        var selfPacket = new ServerEntityPacket
        {
            EntityId = SelfId, Tick = _serverTick,
            State = CharacterStatePacket.FromState(_server.GetState(SelfId), _serverTick),
            HasInput = true, Input = serverSelfInput,
        };
        var oppPacket = new ServerEntityPacket
        {
            EntityId = OpponentId, Tick = _serverTick,
            State = CharacterStatePacket.FromState(_server.GetState(OpponentId), _serverTick),
            HasInput = true, Input = opponentInput,
        };
        _inFlight.Enqueue((_serverTick, selfPacket, oppPacket));

        if (_inFlight.Count > _delayTicks)
        {
            var (_, self, opp) = _inFlight.Dequeue();
            if (!_dropsEnabled || _dropEvery == 0 || _serverTick % _dropEvery != 0)
            {
                _client.ReconcileSelf(self);
                _client.IngestOpponentBatch(new[] { opp });
            }
        }
    }

    public void Step(InputState in1, InputState in2)
        => Step(in1, in1, in2);


    /// <summary>Enable/disable packet loss. The fuzz disables loss during its idle
    /// settle tail so the final reconcile + replay is guaranteed to re-converge.</summary>
    public void SetDropsEnabled(bool enabled) => _dropsEnabled = enabled;

    public CharacterState ServerState(ulong id) => _server.GetState(id);
    public CharacterState ClientState(ulong id) => _client.GetState(id);

    /// <summary>A hit only the authority decides: spawn a hitbox in the server simulation.</summary>
    public void SpawnServerHitbox(Hitbox hitbox) => _server.Resolver.Spawn(hitbox);

    public LocalTrackStats TakeSelfStats() => _client.TakeSelfStats();

    /// <summary>True when the client self state equals the server's on every wire
    /// field after both timelines have settled on the same authoritative inputs. On
    /// divergence, outputs the compared packets so AssertSelfConverged can dump both.</summary>
    public static bool IsSelfConverged(NetplayHarness h, out CharacterStatePacket expected, out CharacterStatePacket actual)
    {
        expected = CharacterStatePacket.FromState(h.ServerState(SelfId));
        actual = CharacterStatePacket.FromState(h.ClientState(SelfId));
        return expected.Equals(actual);
    }

    public static void AssertSelfConverged(NetplayHarness h)
    {
        if (IsSelfConverged(h, out var expected, out var actual))
            return;

        // Field-by-field diff (reflection — failure path only, so the wire struct
        // can grow without maintenance): CharacterStatePacket has no ToString, so
        // Assert.Equal would print two identical type names. A fuzz falsification
        // must name the diverging wire fields to be actionable.
        var diffs = new List<string>();
        foreach (var field in typeof(CharacterStatePacket).GetFields())
        {
            var serverValue = field.GetValue(expected);
            var clientValue = field.GetValue(actual);
            if (!Equals(serverValue, clientValue))
                diffs.Add($"{field.Name}: server={serverValue} client={clientValue}");
        }
        Assert.True(false, "client self state diverged from server:\n" + string.Join("\n", diffs));
    }

    /// <summary>Entity 2 (opponent) must track the server within tolerance. Damage and
    /// knockback may legitimately diverge (PredictedTrack has no self hurtbox), so only
    /// trajectory fields are compared.</summary>
    public static void AssertOpponentConverged(NetplayHarness h, float tolerance = 0.001f)
    {
        var s = h.ServerState(OpponentId);
        var c = h.ClientState(OpponentId);
        TestHelpers.AssertNear(s.PX, c.PX, tolerance);
        TestHelpers.AssertNear(s.PY, c.PY, tolerance);
        TestHelpers.AssertNear(s.PZ, c.PZ, tolerance);
        TestHelpers.AssertNear(s.VX, c.VX, tolerance);
        TestHelpers.AssertNear(s.VY, c.VY, tolerance);
        TestHelpers.AssertNear(s.VZ, c.VZ, tolerance);
        Assert.Equal(s.State, c.State);
        Assert.Equal(s.IsGrounded, c.IsGrounded);
        TestHelpers.AssertNear(s.FacingYaw, c.FacingYaw, tolerance);
    }
}

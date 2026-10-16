using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchClockTests
{
    [Fact]
    public void ReadyPeersStartFiveSecondCountdownAndPlayingClockAdvancesWithoutInput()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        Assert.NotEmpty(ArenaRegistry.All);

        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var first = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var second = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), ArenaRegistry.All.First(arena => arena.Purpose == ArenaPurpose.PvP).Name,
            new[]
            {
                new MatchPlayer(101, CharacterClass.FightGuy, 1),
                new MatchPlayer(102, CharacterClass.Manki, 2),
            }, catalog.Catalog!, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        var firstBaseline = new HashSet<ulong>();
        var secondBaseline = new HashSet<ulong>();
        var firstLatest = new Dictionary<ulong, ServerEntityPacket>();
        var firstInputs = new Dictionary<uint, InputState>();
        var secondLatest = new Dictionary<ulong, ServerEntityPacket>();
        ulong firstEntity = 1;
        uint firstCountdownTick = 0;
        uint firstPlayingTick = 0, secondPlayingTick = 0;
        uint firstClockStartTick = 0, secondClockStartTick = 0;
        uint targetTick = 0;
        bool targetConsumed = false;
        bool targetEdgeCleared = false;

        void Drain(UdpClient peer, HashSet<ulong> baseline,
            Dictionary<ulong, ServerEntityPacket> latest, ulong selfEntity, bool isFirstPeer)
        {
            while (peer.Client.Poll(0, SelectMode.SelectRead))
            {
                var remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] frame = peer.Receive(ref remote);
                if (NetplayControlPacket.TryDeserialize(frame, out var control))
                {
                    if (control.Kind == NetplayControlKind.Clock && control.EntityId == selfEntity &&
                        control.StartTick != 0)
                    {
                        if (isFirstPeer && firstClockStartTick == 0)
                            firstClockStartTick = control.StartTick;
                        else if (!isFirstPeer && secondClockStartTick == 0)
                            secondClockStartTick = control.StartTick;
                    }
                    continue;
                }
                if (frame.Length is not (ServerEntityPacket.NoInputSize or ServerEntityPacket.MaxSize))
                    continue;
                var packet = ServerEntityPacket.Deserialize(frame);
                baseline.Add(packet.EntityId);
                latest[packet.EntityId] = packet;
                if (packet.EntityId != selfEntity)
                    continue;
                if (packet.HasInput)
                    firstInputs[packet.Tick] = packet.Input;
                if (packet.State.MatchState == MatchState.Playing)
                {
                    if (isFirstPeer && firstPlayingTick == 0)
                        firstPlayingTick = packet.Tick;
                    else if (!isFirstPeer && secondPlayingTick == 0)
                        secondPlayingTick = packet.Tick;
                }
                if (!isFirstPeer)
                    continue;
                if (packet.State.MatchState == MatchState.Countdown && firstCountdownTick == 0)
                    firstCountdownTick = packet.Tick;
                if (targetTick == 0 || packet.Tick != targetTick)
                {
                    if (targetTick != 0 && packet.Tick == targetTick + 1 &&
                        packet.Input.ActiveSlot == 0 && !packet.Input.DownPressed)
                        targetEdgeCleared = true;
                    continue;
                }
                targetConsumed = packet.HasInput && packet.Input.DownPressed;
            }
        }

        try
        {
            match.Start();
            var bootstrapFirst = Control(NetplayControlKind.Bootstrap, firstEntity);
            var bootstrapSecond = Control(NetplayControlKind.Bootstrap, 2);
            var deadline = Stopwatch.StartNew();
            var nextBootstrap = DateTime.MinValue;
            var nextReady = DateTime.MinValue;
            var secondBootstrapAt = DateTime.UtcNow.AddMilliseconds(300);
            while (deadline.Elapsed < TimeSpan.FromSeconds(9) &&
                (firstPlayingTick == 0 || secondPlayingTick == 0))
            {
                if (DateTime.UtcNow >= nextBootstrap)
                {
                    first.Send(bootstrapFirst, bootstrapFirst.Length, endpoint);
                    if (DateTime.UtcNow >= secondBootstrapAt)
                        second.Send(bootstrapSecond, bootstrapSecond.Length, endpoint);
                    nextBootstrap = DateTime.UtcNow.AddMilliseconds(250);
                }
                Drain(first, firstBaseline, firstLatest, firstEntity, isFirstPeer: true);
                Drain(second, secondBaseline, secondLatest, 2, isFirstPeer: false);

                if (DateTime.UtcNow >= nextReady)
                {
                    if (firstBaseline.Contains(firstEntity) && firstBaseline.Contains(2))
                    {
                        var state = firstLatest[firstEntity];
                        var ready = Control(NetplayControlKind.Ready, firstEntity, state.Tick);
                        first.Send(ready, ready.Length, endpoint);
                    }
                    if (secondBaseline.Contains(firstEntity) && secondBaseline.Contains(2))
                    {
                        var state = secondLatest[2];
                        var ready = Control(NetplayControlKind.Ready, 2, state.Tick);
                        second.Send(ready, ready.Length, endpoint);
                    }
                    nextReady = DateTime.UtcNow.AddMilliseconds(250);
                }
                if (firstBaseline.Contains(firstEntity) && firstBaseline.Contains(2) &&
                    (!secondBaseline.Contains(firstEntity) || !secondBaseline.Contains(2)))
                {
                    Assert.False(match.HasStartedCountdown,
                        "The server started countdown before the delayed peer's roster baseline and Ready.");
                    Assert.Equal(0u, match.ServerTick);
                }
                Thread.Sleep(1);
            }

            Assert.Contains(firstEntity, firstBaseline);
            Assert.Contains(2UL, firstBaseline);
            Assert.Contains(firstEntity, secondBaseline);
            Assert.Contains(2UL, secondBaseline);
            Assert.NotEqual(0u, firstCountdownTick);
            Assert.NotEqual(0u, secondPlayingTick);
            Assert.Equal(299u, firstPlayingTick - firstCountdownTick);
            Assert.Equal(firstPlayingTick + 1, firstClockStartTick);
            Assert.Equal(secondPlayingTick + 1, secondClockStartTick);

            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(2) &&
                firstLatest[firstEntity].Tick < firstPlayingTick + 30)
            {
                Drain(first, firstBaseline, firstLatest, firstEntity, isFirstPeer: true);
                Thread.Sleep(1);
            }
            uint idleTick = firstLatest[firstEntity].Tick;
            Assert.True(idleTick >= firstPlayingTick + 30);
            Assert.True(firstLatest[firstEntity].HasInput);
            Assert.Equal(default, firstLatest[firstEntity].Input);

            targetTick = match.ServerTick + 20;
            var held = new InputState
            {
                MoveX = 1f,
                Down = true,
                DownPressed = true,
                Jump = true,
                JumpHeld = true,
                FaceToCamera = true,
                ToggleLock = true,
                ActiveSlot = AbilitySlots.Slot1
            };
            byte[] inputFrame = new byte[InputBundle.SizeFor(1)];
            InputBundle.Write(inputFrame, firstEntity, targetTick, new[] { held });
            first.Send(inputFrame, inputFrame.Length, endpoint);

            deadline.Restart();
            while (deadline.Elapsed < TimeSpan.FromSeconds(2) &&
                (!targetConsumed || !targetEdgeCleared || !firstInputs.ContainsKey(targetTick + 7)))
            {
                Drain(first, firstBaseline, firstLatest, firstEntity, isFirstPeer: true);
                Thread.Sleep(1);
            }
            Assert.True(targetConsumed,
                $"Server did not consume the press edge at its exact target tick {targetTick}; server tick={match.ServerTick}, latest state tick={firstLatest[firstEntity].Tick}.");
            Assert.True(targetEdgeCleared, "The following server tick replayed the consumed press edge.");
            Assert.True(firstInputs.TryGetValue(targetTick, out var exactInput));
            Assert.True(exactInput.DownPressed);
            Assert.Equal(1f, exactInput.MoveX);
            for (uint offset = 1; offset <= 6; offset++)
            {
                Assert.True(firstInputs.TryGetValue(targetTick + offset, out var fallback),
                    $"Missing fallback state for server tick {targetTick + offset}.");
                Assert.True(fallback.Down);
                Assert.False(fallback.DownPressed);
                Assert.True(fallback.JumpHeld);
                Assert.False(fallback.Jump);
                Assert.False(fallback.FaceToCamera);
                Assert.False(fallback.ToggleLock);
                Assert.Equal((byte)0, fallback.ActiveSlot);
                Assert.Equal(1f, fallback.MoveX);
            }
            Assert.True(firstInputs.TryGetValue(targetTick + 7, out var neutral));
            Assert.False(neutral.Down);
            Assert.False(neutral.DownPressed);
            Assert.Equal((byte)0, neutral.ActiveSlot);
            Assert.Equal(0f, neutral.MoveX);
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }

    private static byte[] Control(NetplayControlKind kind, ulong entityId, uint tick = 0, uint startTick = 0)
    {
        var frame = new byte[NetplayControlPacket.Size];
        new NetplayControlPacket(kind, entityId, tick, startTick).Serialize(frame);
        return frame;
    }
}

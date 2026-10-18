using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SlopArena.Server;
using SlopArena.Shared;
using Xunit;

namespace SlopArena.Server.Tests;

public class MatchInputProtocolTests
{
    private static byte[] Control(NetplayControlKind kind, ulong entityId, uint tick = 0, uint startTick = 0)
    {
        var frame = new byte[NetplayControlPacket.Size];
        new NetplayControlPacket(kind, entityId, tick, startTick).Serialize(frame);
        return frame;
    }
    [Fact]
    public void InvalidUplinksCannotClaimEndpointOrStartCountdown()
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
        using var sentinel = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var rejected = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var accepted = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var ended = new ManualResetEventSlim();
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), ArenaRegistry.All.First(arena => arena.Purpose == ArenaPurpose.PvP).Name,
            new[] { new MatchPlayer(101, CharacterClass.FightGuy, 1), new MatchPlayer(102, CharacterClass.Manki, 2) },
            catalog.Catalog!, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        byte[] Input(ulong id)
        {
            var bytes = new byte[InputBundle.SizeFor(1)];
            InputBundle.Write(bytes, id, 1, new[] { new InputState { Down = true, DownPressed = true } });
            return bytes;
        }
        WorldFramePacket AwaitState(UdpClient client, byte[]? repeat = null)
        {
            var timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 3000)
            {
                if (repeat != null) client.Send(repeat, repeat.Length, endpoint);
                if (client.Client.Poll(30_000, SelectMode.SelectRead))
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = client.Receive(ref remote);
                    if (WorldFramePacket.TryDeserialize(bytes, out var frame))
                        return frame!;
                }
            }
            throw new TimeoutException("No authoritative state received from local match.");
        }
        try
        {
            match.Start();
            var socketField = typeof(MatchInstance).GetField("_udpServer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.True(SpinWait.SpinUntil(() => socketField.GetValue(match) != null, 3000), "Local UDP listener did not bind.");
            var sentinelBootstrap = Control(NetplayControlKind.Bootstrap, 2);
            sentinel.Send(sentinelBootstrap, sentinelBootstrap.Length, endpoint);
            var invalid = Input(1);
            rejected.Send(invalid, invalid.Length - 1, endpoint); // truncated bundle
            invalid[^1] = 99;
            rejected.Send(invalid, invalid.Length, endpoint); // unsupported version
            var oversized = new byte[invalid.Length + 1];
            Input(1).CopyTo(oversized, 0);
            rejected.Send(oversized, oversized.Length, endpoint);
            var acceptedBootstrap = Control(NetplayControlKind.Bootstrap, 1);
            var state = AwaitState(accepted, acceptedBootstrap);
            var sentinelState = AwaitState(sentinel, sentinelBootstrap);
            Assert.Equal(MatchState.Waiting, state.MatchState);
            accepted.Send(Control(NetplayControlKind.Ready, 1, state.Tick), NetplayControlPacket.Size, endpoint);
            sentinel.Send(Control(NetplayControlKind.Ready, 2, sentinelState.Tick), NetplayControlPacket.Size, endpoint);
            var countdownTimer = Stopwatch.StartNew();
            WorldFramePacket? countdown = null;
            bool sawCountdown = false;
            while (countdownTimer.ElapsedMilliseconds < 1500 && !sawCountdown)
            {
                accepted.Send(acceptedBootstrap, acceptedBootstrap.Length, endpoint);
                if (!accepted.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var received = accepted.Receive(ref remote);
                if (WorldFramePacket.TryDeserialize(received, out var frame) && frame!.MatchState == MatchState.Countdown)
                {
                    countdown = frame;
                    sawCountdown = true;
                }
            }
            Assert.True(sawCountdown, "Ready peers did not enter countdown.");
            var ping = new byte[12];
            ping[0] = (byte)'P'; ping[1] = (byte)'I'; ping[2] = (byte)'N'; ping[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(4), 0x102030405060708);
            accepted.Send(ping, ping.Length, endpoint);
            var pingTimer = Stopwatch.StartNew();
            byte[]? pong = null;
            while (pingTimer.ElapsedMilliseconds < 1500)
            {
                if (!accepted.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var received = accepted.Receive(ref remote);
                if (received.Length == 16 && received[0] == (byte)'P' && received[1] == (byte)'O' &&
                    received[2] == (byte)'N' && received[3] == (byte)'G')
                {
                    pong = received;
                    break;
                }
            }
            Assert.NotNull(pong);
            Assert.Equal(0x102030405060708, BinaryPrimitives.ReadInt64LittleEndian(pong!.AsSpan(4, 8)));

            rejected.Send(ping, ping.Length, endpoint);
            Assert.False(rejected.Client.Poll(200_000, SelectMode.SelectRead), "An unadmitted peer received a ping echo.");
            Assert.Equal(MatchState.Countdown, countdown!.MatchState);
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }
    [Fact]
    public void UdpOpponentFramesCarryWibouKunaiAndSwordHitboxesUntilTheyEnd()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string cooked = Path.Combine(root, "content-cooked");
        var manifest = BuiltInRosterManifestCodec.Load(Path.Combine(cooked, "roster/manifest.json"));
        var packages = manifest.Entries.ToDictionary(x => x.PackageId,
            x => CookedCharacterPackageLoader.LoadDirectory(Path.Combine(cooked, x.PackageId), x.Requirement));
        var catalog = new MatchContentCatalogBuilder().Build(manifest, packages);
        Assert.True(catalog.IsValid, string.Join("; ", catalog.Diagnostics));
        ArenaRegistry.LoadFromDirectory(Path.Combine(root, "data/arenas"));
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
        reservation.Close();
        using var wibou = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var opponent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var ended = new ManualResetEventSlim();
        var arena = ArenaRegistry.All.First(candidate => candidate.Purpose == ArenaPurpose.PvP);
        var match = new MatchInstance(port, Guid.NewGuid().ToString(), arena.Name,
            new[] { new MatchPlayer(101, CharacterClass.Wibou, 1),
                new MatchPlayer(102, CharacterClass.FightGuy, 2) },
            catalog.Catalog!, _ => ended.Set());
        var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        // The opponent's view: its own simulation restored from each received World Frame.
        var view = new ServerSimulation(arena);
        foreach (var (entity, character) in new[] { (1UL, CharacterClass.Wibou), (2UL, CharacterClass.FightGuy) })
        {
            var content = catalog.Catalog!.Resolve(character)!;
            view.RegisterEntity(entity, content.Definition, new CharacterState(), content.BakedAnimation);
        }
        WorldFramePacket? ReceiveFrame(UdpClient client)
        {
            if (!client.Client.Poll(30_000, SelectMode.SelectRead)) return null;
            var remote = new IPEndPoint(IPAddress.Any, 0);
            return WorldFramePacket.TryDeserialize(client.Receive(ref remote), out var frame) ? frame : null;
        }
        void Send(UdpClient client, ulong entity, uint tick, InputState input)
        {
            var frame = new byte[InputBundle.SizeFor(1)];
            InputBundle.Write(frame, entity, tick, new[] { input });
            client.Send(frame, frame.Length, endpoint);
        }
        void SendControl(UdpClient client, NetplayControlKind kind, ulong entity, uint tick = 0, uint startTick = 0)
        {
            var control = Control(kind, entity, tick, startTick);
            client.Send(control, control.Length, endpoint);
        }
        void BootstrapAndReady(UdpClient client, ulong entity)
        {
            var timer = Stopwatch.StartNew();
            var entities = new HashSet<ulong>();
            uint baselineTick = 0;
            while (timer.Elapsed < TimeSpan.FromSeconds(3) && entities.Count < 2)
            {
                SendControl(client, NetplayControlKind.Bootstrap, entity);
                if (!client.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                if (!WorldFramePacket.TryDeserialize(client.Receive(ref remote), out var frame)) continue;
                foreach (var relayed in frame!.Inputs)
                    entities.Add(relayed.EntityId);
                baselineTick = Math.Max(baselineTick, frame.Tick);
            }
            Assert.Equal(2, entities.Count);
            SendControl(client, NetplayControlKind.Ready, entity, baselineTick);
        }
        void SendPing(UdpClient client)
        {
            var ping = new byte[12];
            ping[0] = (byte)'P'; ping[1] = (byte)'I'; ping[2] = (byte)'N'; ping[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(4), Stopwatch.GetTimestamp());
            client.Send(ping, ping.Length, endpoint);
        }
        uint ReadServerTick(UdpClient client)
        {
            long nonce = Stopwatch.GetTimestamp();
            var ping = new byte[12];
            ping[0] = (byte)'P'; ping[1] = (byte)'I'; ping[2] = (byte)'N'; ping[3] = (byte)'G';
            BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(4), nonce);
            client.Send(ping, ping.Length, endpoint);
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(1))
            {
                if (!client.Client.Poll(30_000, SelectMode.SelectRead)) continue;
                var remote = new IPEndPoint(IPAddress.Any, 0);
                var frame = client.Receive(ref remote);
                if (frame.Length == 16 && frame[0] == (byte)'P' &&
                    BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(4, 8)) == nonce)
                    return BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12, 4));
            }
            throw new TimeoutException("No authoritative UDP clock response.");
        }
        match.Start();
        try
        {
            BootstrapAndReady(wibou, 1);
            BootstrapAndReady(opponent, 2);
            var timer = Stopwatch.StartNew();
            bool playing = false;
            uint playingTick = 0;
            long nextHeartbeat = 500;
            while (timer.Elapsed < TimeSpan.FromSeconds(8) && !playing)
            {
                if (timer.ElapsedMilliseconds >= nextHeartbeat)
                {
                    SendPing(wibou);
                    SendPing(opponent);
                    nextHeartbeat += 1000;
                }
                if (ReceiveFrame(opponent) is not { } frame) continue;
                if (frame.MatchState == MatchState.Playing)
                {
                    playing = true;
                    playingTick = frame.Tick;
                }
            }
            Assert.True(playing, "UDP match did not reach Playing.");
            uint nextTick = playingTick + 7;
            for (uint tick = 0; tick < 120; tick++)
            {
                Send(wibou, 1, nextTick++,
                    tick == 0 ? new InputState { ActiveSlot = AbilitySlots.A, IsAiming = true } : default);
                Thread.Sleep(16);
            }
            bool sawThree = false, sawEmptyAfter = false;
            timer.Restart();
            long projectileHeartbeat = 500;
            while (timer.Elapsed < TimeSpan.FromSeconds(4) && !sawEmptyAfter)
            {
                if (timer.ElapsedMilliseconds >= projectileHeartbeat)
                {
                    SendPing(wibou);
                    SendPing(opponent);
                    projectileHeartbeat += 1000;
                }
                if (ReceiveFrame(opponent) is not { } frame) continue;
                view.RestoreSnapshot(frame.Snapshot);
                int kunai = view.Resolver.GetActiveHitboxes().Count(h => h.OwnerId == 1 && h.Active && h.AttackSlot == AbilitySlots.A);
                if (kunai == 3) sawThree = true;
                if (sawThree && kunai == 0) sawEmptyAfter = true;
            }
            Assert.True(sawThree, "The opponent's frames never carried three kunai.");
            Assert.True(sawEmptyAfter, "The opponent's frames never showed the kunai gone.");
            nextTick = ReadServerTick(wibou) + 6;
            for (uint tick = 0; tick < 80; tick++)
            {
                Send(wibou, 1, nextTick++, tick == 0 ? new InputState { ActiveSlot = 3 } : default);
                Thread.Sleep(16);
            }
            bool sawSword = false, sawSwordEmptyAfter = false;
            timer.Restart();
            long swordHeartbeat = 500;
            while (timer.Elapsed < TimeSpan.FromSeconds(4) && !sawSwordEmptyAfter)
            {
                if (timer.ElapsedMilliseconds >= swordHeartbeat)
                {
                    SendPing(wibou);
                    SendPing(opponent);
                    swordHeartbeat += 1000;
                }
                if (ReceiveFrame(opponent) is not { } frame) continue;
                view.RestoreSnapshot(frame.Snapshot);
                bool sword = view.Resolver.GetActiveHitboxes().Any(h => h.OwnerId == 1 && h.Active &&
                    h.SourceEvent.BoneName == "_weapon_hilt" && h.SourceEvent.EndBoneName == "_weapon_tip");
                if (sword) sawSword = true;
                if (sawSword && !sword) sawSwordEmptyAfter = true;
            }
            Assert.True(sawSword, "The opponent's frames never carried the active Wibou sword hitbox.");
            Assert.True(sawSwordEmptyAfter, "The opponent's frames never showed the sword hitbox gone.");
        }
        finally
        {
            match.Stop();
            Assert.True(ended.Wait(3000), "Local match did not stop.");
        }
    }

}

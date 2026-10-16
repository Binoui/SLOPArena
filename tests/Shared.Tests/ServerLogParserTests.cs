using SlopArena.Shared;
using Xunit;

namespace SlopArena.Tests;

/// <summary>
/// Tests for <see cref="ServerLogParser"/>: the pure seam that detects the
/// embedded game server's successful master-server registration (ADR-0005,
/// issue #39) by scanning its stdout for the assigned server-id GUID.
/// </summary>
public class ServerLogParserTests
{
    // ── Known log line shapes from the server ──

    [Fact]
    public void TryParseServerId_ProgramCsShape_ReturnsGuid()
    {
        var line = "Registered successfully (Server ID: 12345678-1234-1234-1234-1234567890ab).";

        bool ok = ServerLogParser.TryParseServerId(line, out var id);

        Assert.True(ok);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-1234567890ab"), id);
    }

    [Fact]
    public void TryParseServerId_RegistrationShape_ReturnsGuid()
    {
        var line = "[Registration] Registered as 'SLOPArena Local Dev' (ID: aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee, IP: 127.0.0.1)";

        bool ok = ServerLogParser.TryParseServerId(line, out var id);

        Assert.True(ok);
        Assert.Equal(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), id);
    }

    [Theory]
    [InlineData("")]                       // empty
    [InlineData("no guid here")]           // plain text
    [InlineData("[Registration] Failed: 404")]
    [InlineData("Server listening on 9876")]
    public void TryParseServerId_NoGuid_ReturnsFalse(string line)
    {
        bool ok = ServerLogParser.TryParseServerId(line, out var id);

        Assert.False(ok);
        Assert.Equal(Guid.Empty, id);
    }

    [Fact]
    public void TryParseServerId_PicksFirstGuid_WhenMultiple()
    {
        // Defensive: a line should only carry one GUID, but the parser must
        // not crash if it ever carries two.
        var line = "ID: 11111111-1111-1111-1111-111111111111 then 22222222-2222-2222-2222-222222222222";

        bool ok = ServerLogParser.TryParseServerId(line, out var id);

        Assert.True(ok);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), id);
    }

    [Fact]
    public void TryParseServerId_Null_DoesNotThrow()
    {
        // The Unity caller may pass a null line from a malformed stderr read.
        Assert.False(ServerLogParser.TryParseServerId(null!, out _));
    }
}

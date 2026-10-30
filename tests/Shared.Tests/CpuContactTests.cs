using SlopArena.Shared.AI;
using Xunit;
namespace SlopArena.Shared.Tests;

public sealed class CpuContactTests
{
    [Theory]
    [InlineData(CpuContactKind.Hit)]
    [InlineData(CpuContactKind.Block)]
    public void ContactsWaitForReactionDelayAndAreConsumedOnce(CpuContactKind kind)
    {
        var target = TestHelpers.NpcState() with { HitstunTicks = 30 };
        var memory = new BotMemory();
        for (int tick = 0; tick <= 18; tick++) memory.ObserveOpponent(target);
        memory.RecordOpponentContact(AbilitySlots.Slot1, kind, target, 0);
        for (int tick = 0; tick < 18; tick++)
        {
            Assert.False(memory.TryGetNewContact(out _));
            memory.ObserveOpponent(target);
        }
        Assert.True(memory.TryGetNewContact(out var contact));
        Assert.Equal(kind, contact.Kind);
        Assert.Equal(kind == CpuContactKind.Hit ? 12 : 0, memory.RemainingHitstun(contact));
        memory.MarkContactEvaluated(contact);
        Assert.False(memory.TryGetNewContact(out _));
    }
    [Fact]
    public void OverflowCannotReuseAnEarlierContactsEpisode()
    {
        var target = TestHelpers.NpcState(); var memory = new BotMemory();
        for (int tick = 0; tick <= 18; tick++) memory.ObserveOpponent(target);
        for (int hit = 0; hit < 8; hit++) memory.RecordOpponentContact(AbilitySlots.Slot1, CpuContactKind.Hit, target, 0);
        memory.ObserveOpponent(target);
        memory.RecordOpponentContact(AbilitySlots.Slot1, CpuContactKind.Block, target, 0);
        for (int tick = 0; tick < 17; tick++) memory.ObserveOpponent(target);
        Assert.True(memory.TryGetNewContact(out var first)); memory.MarkContactEvaluated(first);
        memory.ObserveOpponent(target);
        Assert.True(memory.TryGetNewContact(out var next));
        Assert.True(next.Sequence > first.Sequence); Assert.Equal(CpuContactKind.Block, next.Kind);
    }
}

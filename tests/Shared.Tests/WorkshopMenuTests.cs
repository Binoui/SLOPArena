using SlopArena.Client.UI;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopMenuTests : IDisposable
{
    public WorkshopMenuTests() => UiModalState.ResetForPlaySession();
    public void Dispose() => UiModalState.ResetForPlaySession();
    [Fact]
    public void NewPlayDiscardsUnbalancedModalsWithoutDomainReload()
    {
        UiModalState.Push(); UiModalState.Push(); UiModalState.Push();
        Assert.True(UiModalState.Presented);
        UiModalState.ResetForPlaySession();
        Assert.False(UiModalState.Presented);
        UiModalState.Push(); Assert.True(UiModalState.Presented);
        UiModalState.Pop(); Assert.False(UiModalState.Presented);
        UiModalState.ResetForPlaySession(); UiModalState.Pop(); Assert.False(UiModalState.Presented);
    }
    [Fact]
    public void ChoicesCycleForwardAndBackwardAcrossBothEnds()
    {
        var choice = new MenuChoice(4, 2);
        choice.Move(1); Assert.Equal(3, choice.Index);
        choice.Move(1); Assert.Equal(0, choice.Index);
        choice.Move(-1); Assert.Equal(3, choice.Index);
    }
    [Fact]
    public void OnePadPressWithNativeAndBridgeActivationsAdvancesOnce()
    {
        var choice = new MenuChoice(5);
        Assert.True(choice.Submit(10)); Assert.False(choice.Submit(10.1)); Assert.Equal(1, choice.Index);
        Assert.True(choice.Submit(10.3)); Assert.Equal(2, choice.Index);
    }
    [Fact]
    public void EmptyPickerHasNoSelection()
    { var choice = new MenuChoice(0); choice.Move(1); Assert.Equal(-1, choice.Index); }
    [Theory] [InlineData(0, -1, 3)] [InlineData(3, 1, 0)] [InlineData(-1, 1, 0)] [InlineData(-1, -1, 3)]
    public void FocusWrapsInsideTheAvailableControls(int current, int direction, int expected)
    { Assert.Equal(expected, MenuChoice.NextIndex(current, 4, direction)); }
}

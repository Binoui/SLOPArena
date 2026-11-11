using SlopArena.Client.Workshop;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class WorkshopSessionTests
{
    [Fact]
    public void PreviewRestoresWornItemAndDoesNotSurviveAnotherSession()
    {
        var worn = new ModelSwapChoice("A.glb", "fightguy"); var preview = new ModelSwapChoice("B.glb", "fightguy");
        var choices = new Dictionary<int, ModelSwapChoice> { [1] = worn, [2] = worn }; var session = new ModelSwapSession();
        session.Preview(choices, preview); Assert.Same(preview, choices[1]);
        session.EndPreview(choices); Assert.Same(worn, choices[1]); Assert.Same(worn, choices[2]);
        session.EndPreview(choices); Assert.Same(worn, choices[1]);
        session.Preview(choices, preview); session.Preview(choices, new ModelSwapChoice("C.glb")); session.EndPreview(choices); Assert.Same(worn, choices[1]);
    }
    [Fact]
    public void PreviewWithNoWornItemRestoresBuiltInModel()
    { var choices = new Dictionary<int, ModelSwapChoice>(); var session = new ModelSwapSession(); session.Preview(choices, new ModelSwapChoice("B.glb")); session.EndPreview(choices); Assert.Empty(choices); }
    [Fact]
    public void OnlineChoiceOnlyDressesTheLocalEntityAndCouchKeepsSeats()
    {
        var mine = new ModelSwapChoice("mine.glb"); var couch = new ModelSwapChoice("couch.glb");
        var choices = new Dictionary<int, ModelSwapChoice> { [1] = mine, [2] = couch };
        Assert.Null(ModelSwapSession.ForFighter(choices, 1, true, 2)); Assert.Same(mine, ModelSwapSession.ForFighter(choices, 2, true, 2));
        Assert.Same(couch, ModelSwapSession.ForFighter(choices, 2, false, 1));
    }
    [Fact]
    public void UpdateOnlyContainsFieldsThePlayerChanged()
    {
        var untouched = WorkshopPublishEdits.Changed("title", "description", 2, "title", "description", 2);
        Assert.Null(untouched.Title); Assert.Null(untouched.Description); Assert.Null(untouched.Visibility);
        var title = WorkshopPublishEdits.Changed("old", "description", 2, "new", "description", 2);
        Assert.Equal("new", title.Title); Assert.Null(title.Description); Assert.Null(title.Visibility);
    }
    [Fact]
    public void ForeignOwnershipGetsARecoverableRefusal()
    { Assert.Throws<WorkshopItemUnavailableException>(() => WorkshopPublishEdits.RequireOwner(1, 2)); WorkshopPublishEdits.RequireOwner(1, 1); }
    [Fact]
    public async Task OperationOutlivesItsFirstWaiterAndRetriesShareTheOriginalResult()
    {
        var registry = new WorkshopOperations<ulong>(); var completion = new TaskCompletionSource<ulong>(); int starts = 0;
        Task<ulong> Start() { starts++; return completion.Task; }
        var original = registry.Start("item", Start);
        await Task.Yield(); var retry = registry.Start("item", Start);
        Assert.Same(original, retry); Assert.Equal(1, starts);
        completion.SetResult(3838003137); Assert.Equal(3838003137UL, await retry);
    }
}

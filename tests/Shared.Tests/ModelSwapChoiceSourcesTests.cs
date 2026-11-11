using SlopArena.Client.Workshop;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class ModelSwapChoiceSourcesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "swap-choices-" + Guid.NewGuid().ToString("N"));
    public ModelSwapChoiceSourcesTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    [Fact]
    public void RepeatedFillsDiscoverAddedChangedAndDeletedFolderFiles()
    {
        var swaps = new Dictionary<int, ModelSwapChoice>();
        string first = Path.Combine(_folder, "p1.glb"), second = Path.Combine(_folder, "p2.glb");
        File.WriteAllText(first, "first");
        ModelSwapChoiceSources.Fill(swaps, Array.Empty<string>(), _folder);
        var before = swaps[1];
        File.WriteAllText(first, "changed"); File.WriteAllText(second, "second");
        ModelSwapChoiceSources.Fill(swaps, Array.Empty<string>(), _folder);
        Assert.NotSame(before, swaps[1]); Assert.Equal("changed", File.ReadAllText(swaps[1].GlbPath)); Assert.Contains(2, swaps.Keys);
        File.Delete(first);
        ModelSwapChoiceSources.Fill(swaps, Array.Empty<string>(), _folder);
        Assert.DoesNotContain(1, swaps.Keys); Assert.Contains(2, swaps.Keys);
    }

    [Fact]
    public void PlayerChoicesSurviveWhileLaunchOptionsAreRebuiltPerSeat()
    {
        var selected = new ModelSwapChoice("workshop/model.glb", "bonk");
        var swaps = new Dictionary<int, ModelSwapChoice> { [1] = selected };
        File.WriteAllText(Path.Combine(_folder, "p3.glb"), "third");
        ModelSwapChoiceSources.Fill(swaps, new[] { "-modelSwap", "1=override.glb;2=old.glb" }, _folder);
        Assert.Same(selected, swaps[1]); Assert.Equal("old.glb", swaps[2].GlbPath); Assert.Contains(3, swaps.Keys);
        ModelSwapChoiceSources.Fill(swaps, new[] { "-modelSwap", "2=new.glb" }, _folder);
        Assert.Same(selected, swaps[1]); Assert.Equal("new.glb", swaps[2].GlbPath);
        ModelSwapChoiceSources.Fill(swaps, Array.Empty<string>(), _folder);
        Assert.Same(selected, swaps[1]); Assert.DoesNotContain(2, swaps.Keys); Assert.Contains(3, swaps.Keys);
    }
}

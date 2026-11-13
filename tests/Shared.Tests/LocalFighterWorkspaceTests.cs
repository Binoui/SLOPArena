using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class LocalFighterWorkspaceTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "fighter-workspace-" + Guid.NewGuid().ToString("N"));
    public LocalFighterWorkspaceTests()
    {
        Directory.CreateDirectory(folder);
        var item = FighterItemAdmissionTests.Item();
        foreach (var file in item.Where(x => new[] { "package.json", "character.json", "card.json", "tumbleweed.glb" }.Contains(x.Key)))
            File.WriteAllBytes(Path.Combine(folder, file.Key), file.Value);
        var bindings = JsonNode.Parse(item["client.bindings"])!;
        var animations = new JsonObject();
        foreach (var a in bindings["animations"]!.AsArray()) animations[a!["semanticId"]!.GetValue<string>()] = "fixture";
        File.WriteAllText(Path.Combine(folder, "cook.json"), new JsonObject { ["model"] = "tumbleweed.glb", ["animations"] = animations,
            ["creator"] = "test", ["license"] = "CC0", ["attribution"] = "test", ["aiAssisted"] = false }.ToJsonString());
    }
    public void Dispose() => Directory.Delete(folder, true);
    static byte[] FootMesh() => TestGlb.Rewrite(FighterItemAdmissionTests.Glb(), (root, _) =>
    {
        int foot = root["nodes"]!.AsArray().Select((n, i) => (n, i)).Single(p => p.n!["name"]!.GetValue<string>() == TestGlb.P + "LeftFoot").i;
        var joints = root["skins"]![0]!["joints"]!.AsArray();
        int at = joints.Select((n, i) => (n, i)).Single(p => p.n!.GetValue<int>() == foot).i;
        var old = joints[0]!.DeepClone(); joints[0] = foot; joints[at] = old;
    });
    [Fact] public void CookFitsAdmitsAndAppliesAnIdleSoleSuggestion()
    {
        File.WriteAllBytes(Path.Combine(folder, "tumbleweed.glb"), FootMesh());
        var workspace = LocalFighterWorkspace.Read(folder);
        var cooked = workspace.Cook();
        Assert.NotNull(cooked.Item);
        Assert.NotNull(workspace.SuggestedModelYOffset);
        string output = Path.Combine(folder, "output"); Directory.CreateDirectory(output);
        foreach (var file in cooked.Item!) File.WriteAllBytes(Path.Combine(output, file.Key), file.Value);
        var admission = LocalFighterItems.Read(output);
        Assert.NotNull(admission.Fighter);
        Assert.Empty(admission.Diagnostics);
        workspace.ApplyModelYOffset(folder);
        Assert.Equal(workspace.SuggestedModelYOffset, JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "character.json")))!["presentation"]!["modelYOffset"]!.GetValue<float>());
        Assert.NotEqual(workspace.Stamp, LocalFighterWorkspace.Read(folder).Stamp);
    }
    [Fact] public void StaleSuggestionCannotOverwriteANewerEdit()
    {
        File.WriteAllBytes(Path.Combine(folder, "tumbleweed.glb"), FootMesh());
        var workspace = LocalFighterWorkspace.Read(folder); workspace.Cook();
        File.AppendAllText(Path.Combine(folder, "character.json"), " ");
        Assert.Throws<IOException>(() => workspace.ApplyModelYOffset(folder));
    }
    [Fact] public void EveryMissingClipHasItsSemanticLocation()
    {
        var cook = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "cook.json")))!;
        var ids = cook["animations"]!.AsObject().Select(x => x.Key).Take(3).ToArray();
        foreach (string id in ids) cook["animations"]![id] = "missing." + id;
        File.WriteAllText(Path.Combine(folder, "cook.json"), cook.ToJsonString());
        var result = LocalFighterWorkspace.Read(folder).Cook();
        Assert.Null(result.Item);
        foreach (string id in ids) Assert.Contains(result.Diagnostics, d => d.Path == "cook.json.animations." + id && d.Message.Contains("missing." + id));
    }
    [Fact] public void CompilerErrorsDoNotHideMissingClips()
    {
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "character.json")))!;
        character["movement"]!["maxJumps"] = 256;
        File.WriteAllText(Path.Combine(folder, "character.json"), character.ToJsonString());
        var cook = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "cook.json")))!;
        cook["animations"]!["anim.idle"] = "missing-idle";
        File.WriteAllText(Path.Combine(folder, "cook.json"), cook.ToJsonString());
        var result = LocalFighterWorkspace.Read(folder).Cook();
        Assert.Null(result.Item);
        Assert.Contains(result.Diagnostics, d => d.Path.Contains("maxJumps"));
        Assert.Contains(result.Diagnostics, d => d.Path == "cook.json.animations.anim.idle");
    }
    [Fact] public void CapsuleRadiusUsesTheBodyRegionAndRejectsTheOutlier()
    {
        var points = Enumerable.Range(0, 10).Select(_ => new System.Numerics.Vector3(.2f, 1, 0))
            .Append(new System.Numerics.Vector3(10, 1, 0)).ToArray();
        var fit = PortableMeshMeasurements.FitCapsules(points, points.Select(_ => "head").ToArray(),
            id => id == "bone.head" ? new System.Numerics.Vector3(0, 1, 0) : System.Numerics.Vector3.Zero);
        Assert.Equal(.2f, fit[0].Radius);
        Assert.Equal(14, fit.Length);
    }
    [Fact] public void UnchangedReadsHaveTheSameStampAndEditsInvalidateIt()
    {
        var first = LocalFighterWorkspace.Read(folder);
        Assert.Equal(first.Stamp, LocalFighterWorkspace.Read(folder).Stamp);
        File.AppendAllText(Path.Combine(folder, "character.json"), " ");
        Assert.NotEqual(first.Stamp, LocalFighterWorkspace.Read(folder).Stamp);
    }
    [Fact] public void SyntheticSoleFollowsVisualScale()
    {
        var a = PortableMeshMeasurements.Measure(FootMesh(), null, "fixture", 1);
        var b = PortableMeshMeasurements.Measure(FootMesh(), null, "fixture", 2);
        Assert.Equal(-.05f, a.ModelYOffset, 3);
        Assert.Equal(a.ModelYOffset * 2, b.ModelYOffset, 3);
        Assert.Equal(14, a.Hurtboxes.Count);
        Assert.All(a.Hurtboxes, c => Assert.True(c.Radius >= .015f));
    }
}

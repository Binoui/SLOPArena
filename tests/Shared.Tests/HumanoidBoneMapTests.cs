using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>Model swap bone map (ADR-0065): Mixamo names with or without a prefix, and the humanoid.json map file.</summary>
public class HumanoidBoneMapTests
{
    // The 41 skin joints of the raw Tripo Dusty, exported by Blender at default settings (Workshop step 0.1b).
    private static readonly string[] TripoJoints =
    {
        "mixamorig:Hips", "mixamorig:LeftUpLeg", "mixamorig:LeftLeg", "mixamorig:LeftFoot", "mixamorig:LeftToeBase",
        "mixamorig:LeftToe_End", "mixamorig:RightUpLeg", "mixamorig:RightLeg", "mixamorig:RightFoot",
        "mixamorig:RightToeBase", "mixamorig:RightToe_End", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2",
        "mixamorig:LeftShoulder", "mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
        "mixamorig:LeftHandIndex1", "mixamorig:LeftHandIndex2", "mixamorig:LeftHandIndex3", "mixamorig:LeftHandIndex4",
        "mixamorig:LeftHandThumb1", "mixamorig:LeftHandThumb2", "mixamorig:LeftHandThumb3", "mixamorig:LeftHandThumb4",
        "mixamorig:Neck", "mixamorig:Head", "mixamorig:HeadTop_End", "mixamorig:RightShoulder", "mixamorig:RightArm",
        "mixamorig:RightForeArm", "mixamorig:RightHand", "mixamorig:RightHandIndex1", "mixamorig:RightHandIndex2",
        "mixamorig:RightHandIndex3", "mixamorig:RightHandIndex4", "mixamorig:RightHandThumb1",
        "mixamorig:RightHandThumb2", "mixamorig:RightHandThumb3", "mixamorig:RightHandThumb4",
    };

    [Fact]
    public void TripoRig_MapsEveryHumanBoneItHas()
    {
        var map = HumanoidBoneMap.FromMixamoNames(TripoJoints);

        Assert.Equal(34, map.NodeByHuman.Count);
        Assert.Empty(map.MissingRequired);
        Assert.Equal("mixamorig:Spine2", map.NodeFor("UpperChest"));
        Assert.Equal("mixamorig:LeftHandThumb1", map.NodeFor("LeftThumbProximal"));
        Assert.Null(map.NodeFor("LeftLittleProximal"));
    }

    [Theory]
    [InlineData("Hips")]
    [InlineData("mixamorig1:Hips")]
    [InlineData("Character:mixamorig:Hips")]
    [InlineData("HIPS")]
    public void Prefixes_AndCase_AreIgnored(string hips)
    {
        var map = HumanoidBoneMap.FromMixamoNames(new[] { hips, "Spine1" });

        Assert.Equal(hips, map.NodeFor("Hips"));
        Assert.Equal("Spine1", map.NodeFor("Chest"));
    }

    [Fact]
    public void FirstNodeWithABareName_Wins()
    {
        var map = HumanoidBoneMap.FromMixamoNames(new[] { "mixamorig:Head", "Head" });

        Assert.Equal("mixamorig:Head", map.NodeFor("Head"));
    }

    [Fact]
    public void MissingRequiredBones_AreNamed()
    {
        var map = HumanoidBoneMap.FromMixamoNames(TripoJoints.Where(x => x is not ("mixamorig:LeftHand" or "mixamorig:Spine")));

        Assert.Equal(new[] { "Spine", "LeftHand" }, map.MissingRequired);
    }

    [Fact]
    public void MapFile_IsRead()
    {
        Assert.True(HumanoidBoneMap.TryParseMapFile(
            """{"bones":[{"human":"Hips","node":"pelvis"},{"human":"Head","node":"head_jnt"}]}""", out var map, out var error), error);

        Assert.Equal("pelvis", map!.NodeFor("Hips"));
        Assert.Equal("head_jnt", map.NodeFor("Head"));
        Assert.Contains("LeftHand", map.MissingRequired);
    }

    [Theory]
    [InlineData("""{"bones":[],"version":1}""", "unknown field \"version\"")]
    [InlineData("""{"bones":[{"human":"Hips","node":"a","side":"l"}]}""", "unknown field \"side\"")]
    [InlineData("""{"bones":[{"human":"Tail","node":"a"}]}""", "\"Tail\", which is not a Unity human bone")]
    [InlineData("""{"bones":[{"human":"Hips","node":"a"},{"human":"Hips","node":"b"}]}""", "maps Hips a second time")]
    [InlineData("""{"bones":[{"human":"Hips","node":"a"},{"human":"Spine","node":"a"}]}""", "already plays Hips")]
    [InlineData("""{"bones":[{"human":"Hips"}]}""", "needs both")]
    [InlineData("""{"bones":[{"human":"Hips","node":3}]}""", "must be a string")]
    [InlineData("""{"bone":[]}""", "unknown field \"bone\"")]
    [InlineData("""[]""", "must be an object")]
    [InlineData("""{"bones":[""", "not valid JSON")]
    public void BadMapFiles_AreRefusedWithTheReason(string json, string reason)
    {
        Assert.False(HumanoidBoneMap.TryParseMapFile(json, out var map, out var error));

        Assert.Null(map);
        Assert.Contains(reason, error);
    }
}

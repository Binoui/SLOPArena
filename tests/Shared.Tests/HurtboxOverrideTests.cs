using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for the Ability Lab hurtbox override (spec #119): path derivation, the
/// JSON format round-trip, malformed-input fallback, baked-order validation, and
/// the apply-as-clone semantics hosts use at entity registration.
/// </summary>
public class HurtboxOverrideTests
{
    private static readonly HurtboxBoneDef[] Defs =
    {
        new("mixamorig:Head", 0, 0, 0, 0.22f),
        new("mixamorig:Spine2", 0.1f, 0, 0.05f, 0.26f),
    };

    private static BakedAnimationData BakedWith(params string[] boneNames)
        => BakedAnimationData.LoadFromBin(MakeBin(boneNames));

    private static byte[] MakeBin(string[] boneNames)
    {
        var bytes = new System.Collections.Generic.List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        bytes.AddRange(System.BitConverter.GetBytes(1u));
        bytes.AddRange(System.BitConverter.GetBytes((uint)boneNames.Length));
        bytes.AddRange(System.BitConverter.GetBytes(1u));
        foreach (var name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(System.BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }
        bytes.AddRange(System.BitConverter.GetBytes(0u)); // anim name len = 0 → ""
        bytes.AddRange(System.BitConverter.GetBytes(1u)); // 1 frame
        for (int i = 0; i < boneNames.Length * 3; i++)
            bytes.AddRange(System.BitConverter.GetBytes(0f));
        return bytes.ToArray();
    }

    [Fact]
    public void CapsuleOverrideKeepsItsEndpointAndOffset()
    {
        var capsule = new HurtboxBoneDef("start", .1f, .2f, .3f, .2f, "end", -.1f, .4f, .6f);
        string json = HurtboxOverride.Serialize(CharacterClass.Manki, new[] { capsule });
        Assert.True(HurtboxOverride.TryParse(json, out _, out var parsed));
        Assert.Equal(capsule, Assert.Single(parsed!));
    }

    [Fact]
    public void SerializeParse_RoundTrips()
    {
        string json = HurtboxOverride.Serialize(CharacterClass.Manki, Defs);

        Assert.True(HurtboxOverride.TryParse(json, out var character, out var parsed));
        Assert.Equal(CharacterClass.Manki, character);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Length);
        Assert.Equal("mixamorig:Head", parsed[0].BoneName);
        Assert.Equal(0.22f, parsed[0].Radius, 5);
        Assert.Equal(0.1f, parsed[1].OffX, 5);
        Assert.Equal(0.05f, parsed[1].OffZ, 5);
        Assert.Contains("\"character\": \"Manki\"", json); // stable shape for hand edits
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsFalse()
    {
        Assert.False(HurtboxOverride.TryParse("{not json", out _, out var defs));
        Assert.Null(defs);
    }

    [Fact]
    public void TryParse_EmptyBoneList_ReturnsFalse()
    {
        string json = HurtboxOverride.Serialize(CharacterClass.Manki, System.Array.Empty<HurtboxBoneDef>());
        Assert.False(HurtboxOverride.TryParse(json, out _, out _));
    }

    [Fact]
    public void TryParse_UnknownCharacter_ReturnsFalse()
    {
        string json = HurtboxOverride.Serialize(CharacterClass.Manki, Defs).Replace("Manki", "Nobody");
        Assert.False(HurtboxOverride.TryParse(json, out _, out _));
    }

    [Fact]
    public void TryParse_MissingBoneName_ReturnsFalse()
    {
        string json = HurtboxOverride.Serialize(CharacterClass.Manki, Defs).Replace("mixamorig:Head", "");
        Assert.False(HurtboxOverride.TryParse(json, out _, out _));
    }

    [Fact]
    public void OverridePathFor_DerivesFromBakedPath()
    {
        var def = new CharacterDefinition { BakedDataPath = "res://data/manki_skeleton.bin" };
        Assert.Equal("res://data/manki_hurtboxes.json", HurtboxOverride.OverridePathFor(def));

        var noBake = new CharacterDefinition { BakedDataPath = "" };
        Assert.Null(HurtboxOverride.OverridePathFor(noBake));

        var custom = new CharacterDefinition { BakedDataPath = "data/foo.bin" };
        Assert.Equal("data/foo_hurtboxes.json", HurtboxOverride.OverridePathFor(custom));
    }

    [Fact]
    public void ValidateOrder_MatchesBakedOrder()
    {
        var baked = BakedWith("mixamorig:Head", "mixamorig:Spine2");
        Assert.True(HurtboxOverride.ValidateOrder(Defs, baked));

        var wrongCount = BakedWith("mixamorig:Head");
        Assert.False(HurtboxOverride.ValidateOrder(Defs, wrongCount));

        var wrongOrder = BakedWith("mixamorig:Spine2", "mixamorig:Head");
        Assert.False(HurtboxOverride.ValidateOrder(Defs, wrongOrder));
    }

    [Fact]
    public void Apply_ClonesDef_LeavesOriginalUntouched()
    {
        var original = new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HurtboxBoneDefs = new[] { new HurtboxBoneDef("mixamorig:Head", 0, 0, 0, 0.2f) },
        };
        var overridden = new[] { new HurtboxBoneDef("mixamorig:Head", 0, 0, 0, 0.9f) };

        var clone = HurtboxOverride.Apply(original, overridden);

        Assert.NotSame(original, clone);
        Assert.Equal(0.9f, clone.HurtboxBoneDefs![0].Radius, 5);      // override applied
        Assert.Equal(0.2f, original.HurtboxBoneDefs![0].Radius, 5);   // original untouched
        Assert.Equal(1.5f, clone.CapsuleHeight, 5);                   // other fields preserved
    }
}

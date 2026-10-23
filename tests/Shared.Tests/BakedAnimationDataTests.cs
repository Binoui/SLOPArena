using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace SlopArena.Shared.Tests;

public class BakedAnimationDataTests
{
    /// <summary>
    /// Build a byte array in the .bin format that BakedAnimationData.LoadFromBin expects.
    /// Matches the format documented in SlopArenaBaker.cs.
    /// </summary>
    private static byte[] BuildTestBin(string[] boneNames, (string name, int frameCount)[] anims, float[] positions)
    {
        var bytes = new List<byte>();

        // Magic: "SKEL"
        bytes.AddRange(Encoding.ASCII.GetBytes("SKEL"));
        // Version = 1
        bytes.AddRange(BitConverter.GetBytes(1u));
        // Bone count
        bytes.AddRange(BitConverter.GetBytes((uint)boneNames.Length));
        // Anim count
        bytes.AddRange(BitConverter.GetBytes((uint)anims.Length));

        // Bone names
        foreach (var name in boneNames)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            bytes.AddRange(BitConverter.GetBytes((uint)nameBytes.Length));
            bytes.AddRange(nameBytes);
        }

        // Animations
        int posIdx = 0;
        foreach (var (animName, frameCount) in anims)
        {
            byte[] animNameBytes = Encoding.UTF8.GetBytes(animName);
            bytes.AddRange(BitConverter.GetBytes((uint)animNameBytes.Length));
            bytes.AddRange(animNameBytes);
            bytes.AddRange(BitConverter.GetBytes((uint)frameCount));

            int stride = boneNames.Length * 3;
            for (int f = 0; f < frameCount; f++)
            {
                for (int b = 0; b < stride; b++)
                {
                    float val = posIdx < positions.Length ? positions[posIdx] : 0f;
                    bytes.AddRange(BitConverter.GetBytes(val));
                    posIdx++;
                }
            }
        }

        return bytes.ToArray();
    }

    [Fact]
    public void LoadFromBin_ValidFile_ParsesCorrectly()
    {
        var boneNames = new[] { "mixamorig_Head", "mixamorig_Hips", "mixamorig_LeftFoot" };
        var anims = new[] { ("idle", 3), ("run", 2) };
        // idle frame 0: Head(0,0.95,0), Hips(0,0.6,0), LeftFoot(0.08,0.0,0.05)
        // idle frame 1: Head(0,0.96,0.01), Hips(0,0.6,0), LeftFoot(0.08,0.0,0.06)
        // idle frame 2: Head(0,0.95,0), Hips(0,0.6,0), LeftFoot(0.08,0.01,0.05)
        // run frame 0: Head(0.01,0.95,0.1), Hips(0,0.6,0.05), LeftFoot(0.08,-0.02,0.1)
        // run frame 1: Head(0.01,0.94,0.15), Hips(0,0.6,0.1), LeftFoot(0.08,-0.01,0.12)
        var positions = new float[]
        {
            0f, 0.95f, 0f,    0f, 0.6f, 0f,    0.08f, 0.0f, 0.05f,
            0f, 0.96f, 0.01f, 0f, 0.6f, 0f,    0.08f, 0.0f, 0.06f,
            0f, 0.95f, 0f,    0f, 0.6f, 0f,    0.08f, 0.01f, 0.05f,
            0.01f, 0.95f, 0.1f, 0f, 0.6f, 0.05f, 0.08f, -0.02f, 0.1f,
            0.01f, 0.94f, 0.15f, 0f, 0.6f, 0.1f, 0.08f, -0.01f, 0.12f,
        };

        var data = BuildTestBin(boneNames, anims, positions);
        var baked = BakedAnimationData.LoadFromBin(data);

        Assert.NotNull(baked);
        Assert.Equal(3, baked.BoneNames.Count);
        Assert.Equal("mixamorig_Head", baked.BoneNames[0]);
        Assert.Equal("mixamorig_Hips", baked.BoneNames[1]);
        Assert.Equal("mixamorig_LeftFoot", baked.BoneNames[2]);

        Assert.Equal(2, baked.Animations.Count);
        Assert.Equal("idle", baked.Animations[0].Name);
        Assert.Equal(3, baked.Animations[0].FrameCount);
        Assert.Equal("run", baked.Animations[1].Name);
        Assert.Equal(2, baked.Animations[1].FrameCount);
    }

    [Fact]
    public void LoadFromBin_PositionValues_AreCorrect()
    {
        var boneNames = new[] { "mixamorig_Head", "mixamorig_Hips", "mixamorig_LeftFoot" };
        var anims = new[] { ("idle", 1) };
        var positions = new float[]
        {
            0.1f, 0.95f, -0.02f,  0f, 0.6f, 0f,  0.08f, 0.0f, 0.05f,
        };

        var data = BuildTestBin(boneNames, anims, positions);
        var baked = BakedAnimationData.LoadFromBin(data);

        // Head at frame 0
        Assert.True(baked.GetBonePosition("idle", 0, 0, out float hx, out float hy, out float hz));
        Assert.Equal(0.1f, hx, 5);
        Assert.Equal(0.95f, hy, 5);
        Assert.Equal(-0.02f, hz, 5);

        // Hips at frame 0
        Assert.True(baked.GetBonePosition("idle", 0, 1, out float sx, out float sy, out float sz));
        Assert.Equal(0f, sx, 5);
        Assert.Equal(0.6f, sy, 5);
        Assert.Equal(0f, sz, 5);

        // LeftFoot at frame 0
        Assert.True(baked.GetBonePosition("idle", 0, 2, out float fx, out float fy, out float fz));
        Assert.Equal(0.08f, fx, 5);
        Assert.Equal(0f, fy, 5);
        Assert.Equal(0.05f, fz, 5);
    }

    [Fact]
    public void LoadFromBin_MissingAnimation_ReturnsFalse()
    {
        var boneNames = new[] { "mixamorig_Head" };
        var anims = new[] { ("idle", 1) };
        var positions = new float[] { 0f, 0.95f, 0f };

        var data = BuildTestBin(boneNames, anims, positions);
        var baked = BakedAnimationData.LoadFromBin(data);

        Assert.False(baked.GetBonePosition("nonexistent", 0, 0, out _, out _, out _));
    }

    [Fact]
    public void LoadFromBin_FrameClamping_ClampsOutOfRange()
    {
        var boneNames = new[] { "mixamorig_Head" };
        var anims = new[] { ("idle", 2) };
        var positions = new float[]
        {
            0f, 0.95f, 0f,
            0.1f, 0.96f, 0.01f,
        };

        var data = BuildTestBin(boneNames, anims, positions);
        var baked = BakedAnimationData.LoadFromBin(data);

        // frameIndex = 5 should clamp to last frame (1)
        Assert.True(baked.GetBonePosition("idle", 5, 0, out float x, out float y, out float z));
        Assert.Equal(0.1f, x, 5); // Should be frame 1's value
        Assert.Equal(0.96f, y, 5);
    }

    [Fact]
    public void LoadFromBin_InvalidMagic_Throws()
    {
        var badBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 };
        Assert.Throws<Exception>(() => BakedAnimationData.LoadFromBin(badBytes));
    }

    [Fact]
    public void GetMinBoneY_ReturnsLowestBoneY()
    {
        string[] boneNames = { "mixamorig:Head", "mixamorig:Hips", "mixamorig:RightFoot" };
        // Hips=0, Head above, Foot below
        var positions = new float[]
        {
            0f, 0.5f, 0f,  // Head
            0f, 0f,   0f,  // Hips
            0f, -0.4f, 0f, // Foot
        };
        var bin = BuildTestBin(boneNames, new[] { ("idle", 1) }, positions);
        var baked = BakedAnimationData.LoadFromBin(bin);

        float minY = baked.GetMinBoneY();

        Assert.Equal(-0.4f, minY, 3);
    }

    [Fact]
    public void GetMinBoneY_NoIdleAnim_ReturnsZero()
    {
        string[] boneNames = { "mixamorig:Hips" };
        var positions = new float[] { 0f, 0f, 0f };
        var bin = BuildTestBin(boneNames, new[] { ("run", 1) }, positions);
        var baked = BakedAnimationData.LoadFromBin(bin);

        float minY = baked.GetMinBoneY();

        Assert.Equal(0f, minY);
    }

    [Fact]
    public void BuildEntitiesFromState_HipsHurtboxAtHipHeight()
    {
        var def = new CharacterDefinition
        {
            CapsuleHeight = 1.5f,
            HipHeight = 0.50f,
            HurtboxBoneDefs = new HurtboxBoneDef[]
            {
                new("mixamorig:Hips", 0, 0, 0, 0.3f),
            },
        };
        string[] boneNames = { "mixamorig:Hips" };
        var positions = new float[] { 0f, 0f, 0f }; // Hips at (0,0,0) in baked data
        var bin = BuildTestBin(boneNames, new[] { ("idle", 1) }, positions);
        var baked = BakedAnimationData.LoadFromBin(bin);

        var state = new CharacterState
        {
            PX = 0, PY = 0.75f, PZ = 0, // capsule center = CapsuleHeight/2
            FacingYaw = 0,
        };

        var entities = ServerSimulation.BuildEntitiesFromState(state, def, baked, "idle", 0);

        Assert.Single(entities);
        Assert.Equal(0.50f, entities[0].PosY, 3); // HipHeight, not 0
    }
}

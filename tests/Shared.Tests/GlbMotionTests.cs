using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static SlopArena.Shared.Tests.TestGlb;

namespace SlopArena.Shared.Tests;

public sealed class GlbMotionTests
{
    private static GlbMotionRig Rig(params Clip[] clips) => GlbMotionRig.Read(Build(clips), null);
    private static GlbNodePose Pose(GlbMotionRig rig, GlbClip clip, int tick, string node)
    {
        var poses = new GlbNodePose[rig.NodeCount];
        rig.SampleLocal(clip, tick, poses);
        return poses[rig.FindNode(node)];
    }
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static string Refusal(params Clip[] clips) => Assert.Throws<GlbMotionException>(() => Rig(clips).Clip(clips[0].Name)).Code;
    private static Channel Arm(float[] times, Func<int, double> tangent) => new(P + "LeftArm", "rotation", times, times.SelectMany((_, k) => Turn(2, tangent(k))).ToArray());

    [Fact]
    public void ATickReadsItsStoredKeyInGameAxes()
    {
        var rig = Rig(Wave("wave", 12));
        var clip = rig.Clip("wave");
        Assert.Equal(12, clip.FrameCount);
        for (int tick = 0; tick < 12; tick++)
        {
            var q = Turn(2, (tick + 1) / 12.0);
            var arm = Pose(rig, clip, tick, P + "LeftArm");
            Assert.Equal(new[] { Bits(q[0]), Bits(-q[1]), Bits(-q[2]), Bits(q[3]) }, new[] { Bits(arm.QX), Bits(arm.QY), Bits(arm.QZ), Bits(arm.QW) });
            var hips = Pose(rig, clip, tick, P + "Hips");
            Assert.Equal(Bits(-0f), Bits(hips.TX));
            Assert.Equal(Bits(1 - tick / 48f), Bits(hips.TY));
        }
        var rest = Pose(rig, clip, 3, P + "RightArm");
        Assert.Equal((0.1f, 0f, 1f), (rest.TX, rest.TY, rest.QW));
    }

    [Fact]
    public void ChannelsClampOutsideTheirKeysAndTheClipMayStartLate()
    {
        var times = Ticks(10, .5f);
        var late = Ticks(4, .5f + 3 / 60f);
        var clip = new Clip("late", 10 / 60f, new[] { Arm(times, k => k / 10.0), new Channel(P + "Hips", "translation", late, late.SelectMany((_, k) => V(0, 1 + k, 0)).ToArray()) });
        var rig = Rig(clip);
        var read = rig.Clip("late");
        Assert.Equal(1f, Pose(rig, read, 0, P + "Hips").TY);
        Assert.Equal(1f, Pose(rig, read, 3, P + "Hips").TY);
        Assert.Equal(4f, Pose(rig, read, 9, P + "Hips").TY);
        Assert.Equal(Bits(Turn(2, .5)[2]), Bits(-Pose(rig, read, 5, P + "LeftArm").QZ));
    }

    [Theory]
    [InlineData(35, 35)]
    [InlineData(35.4, 36)]
    [InlineData(.2, 1)]
    public void FrameCountIsTheCeilingOfTheRecordedDuration(double ticks, int frames)
    {
        float duration = (float)(ticks / 60.0);
        var times = Ticks((int)Math.Floor(ticks) + 1);
        var rig = Rig(new Clip("c", duration, new[] { Arm(times.Take(Math.Max(1, (int)Math.Floor(ticks))).ToArray(), k => k / 100.0) }));
        Assert.Equal(frames, rig.Clip("c").FrameCount);
    }

    [Fact]
    public void TheTerminalEndpointIsNotSampled()
    {
        var rig = Rig(new Clip("c", 10 / 60f, new[] { Arm(Ticks(11), k => k / 20.0) }));
        var clip = rig.Clip("c");
        Assert.Equal(10, clip.FrameCount);
        Assert.Equal(Bits(Turn(2, 9 / 20.0)[3]), Bits(Pose(rig, clip, 9, P + "LeftArm").QW));
    }

    [Fact]
    public void ExtrasMustRecordDurationAndAgreeingFrameCount()
    {
        var keys = new[] { Arm(Ticks(10), k => k / 10.0) };
        Assert.Equal("glb.clip.extras", Refusal(new Clip("c", 10 / 60f, keys, Extras: false)));
        Assert.Equal("glb.clip.frame-count", Refusal(new Clip("c", 10 / 60f, keys, FrameCount: 11)));
        Assert.Equal("glb.clip.extras", Refusal(new Clip("c", 10 / 60f, keys, ExtraSlop: new JsonObject { ["fps"] = 60 })));
        Assert.Equal("glb.clip.extras", Refusal(new Clip("c", 10 / 60f, keys, ExtraSlop: new JsonObject { ["durationSecondsBits"] = Bits(-1f) })));
        Assert.Equal("glb.clip.keys", Refusal(new Clip("c", 8 / 60f, keys)));
        Assert.Equal("glb.clip.duration", Refusal(new Clip("c", 8 / 60f, new[] { Arm(new[] { 0f, 9 / 60f }, k => k / 10.0) }, Interpolation: "STEP")));
    }

    [Fact]
    public void KeysMustSitOnTicks()
    {
        var offGrid = Ticks(10).Select(t => t + 1 / 180f).Prepend(0f).Take(10).ToArray();
        Assert.Equal("glb.clip.timing", Refusal(new Clip("c", 10 / 60f, new[] { Arm(offGrid, k => k / 10.0) })));
        var thirty = Enumerable.Range(0, 5).Select(k => (float)(k / 30.0)).ToArray();
        Assert.Equal("glb.clip.timing", Refusal(new Clip("c", 10 / 60f, new[] { Arm(thirty, k => k / 10.0) })));
        Assert.Equal("glb.clip.interpolation", Refusal(new Clip("c", 10 / 60f, new[] { Arm(Ticks(10), k => k / 10.0) }, Interpolation: "CUBICSPLINE")));
    }

    [Fact]
    public void GapsAreExactWhenTheKeysAgreeOrStep()
    {
        float[] ends = { 0f, 9 / 60f };
        var rig = Rig(new Clip("held", 10 / 60f, new[] { Arm(ends, _ => .25) }), new Clip("step", 10 / 60f, new[] { Arm(ends, k => k / 4.0) }, Interpolation: "STEP"));
        Assert.Equal(Bits(Turn(2, .25)[3]), Bits(Pose(rig, rig.Clip("held"), 5, P + "LeftArm").QW));
        Assert.Equal(Bits(Turn(2, 0)[3]), Bits(Pose(rig, rig.Clip("step"), 8, P + "LeftArm").QW));
        Assert.Equal(Bits(Turn(2, .25)[3]), Bits(Pose(rig, rig.Clip("step"), 9, P + "LeftArm").QW));
        Assert.Equal("glb.clip.timing", Refusal(new Clip("lerp", 10 / 60f, new[] { Arm(ends, k => k / 4.0) })));
    }

    private static CookedCharacterDefinition Tumbleweed()
    {
        string root = Path.Combine(CookedPackageEdits.Root, "tests/Shared.Tests/Fixtures/Tumbleweed");
        var result = CharacterPackageCompiler.CompilePortable(File.ReadAllText(Path.Combine(root, "package.json")), File.ReadAllText(Path.Combine(root, "character.json")));
        return result.CookedPackage!.Definition;
    }

    private static BakedAnimationData Bake(byte[] glb, string clip = "fixture")
    {
        var item = FighterItemAdmissionTests.Item(model: glb, animation: _ => new JsonObject { ["kind"] = "glb", ["clipName"] = clip });
        return BakedAnimationData.LoadFromBin(item[CharacterPackageAssembler.PosePath]);
    }

    [Fact]
    public void BakedPointsAreHipsRelativeInGameAxesAndScaled()
    {
        var times = Ticks(3);
        var glb = Build(new[] { new Clip("raise", 3 / 60f, new[] { Arm(times, _ => 1.0) }) });
        var baked = Bake(glb, "raise");
        Assert.Equal(CharacterBoneRegistry.HumanoidPoints.Where(x => x.Required).Select(x => x.BakedName).Append("_hips_offset"), baked.BoneNames);
        var definition = Tumbleweed();
        float scale = definition.Presentation.VisualScale;
        // A 90-degree turn about glTF Z lifts the 0.5 m forearm and hand straight up; game axes reflect X.
        Assert.True(baked.TryGetPoint(baked.FindAnimIndex("anim.idle"), 1, baked.FindBoneIndex("mixamorig:LeftHand"), out float x, out float y, out float z));
        Assert.Equal(-.15f * scale, x, 5);
        Assert.Equal(.9f * scale, y, 5);
        Assert.Equal(0f, z, 6);
        Assert.True(baked.TryGetPoint(baked.FindAnimIndex("anim.idle"), 0, baked.HipsOffsetIndex, out float hx, out float hy, out float hz));
        Assert.Equal((0, Bits(definition.Presentation.ModelYOffset), 0), (Bits(hx), Bits(hy), Bits(hz)));
        Assert.Equal(baked.Animations.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal), baked.Animations.Select(a => a.Name));
    }

    [Fact]
    public void ARestPoseStoredLyingDownIsStoodUp()
    {
        var upright = Bake(FighterItemAdmissionTests.Glb());
        var lying = Bake(FighterItemAdmissionTests.Glb(Turn(0, -1)));
        for (int a = 0; a < upright.Animations.Count; a++)
            for (int f = 0; f < upright.Animations[a].FrameCount; f++)
                for (int v = 0; v < upright.BoneNames.Count * 3; v++)
                    Assert.Equal(upright.Animations[a].Frame(f)[v], lying.Animations[a].Frame(f)[v], 5);
    }

    [Fact]
    public void AttachmentPointsAreTheirSameNamedMarkerNodes()
    {
        var withMarker = FighterItemAdmissionTests.Item(attachments: new[] { "_weapon_tip" });
        var baked = BakedAnimationData.LoadFromBin(withMarker[CharacterPackageAssembler.PosePath]);
        Assert.Equal("_weapon_tip", baked.BoneNames[^2]);
        Assert.True(FighterItemAdmissionTests.ReadItem(withMarker).IsAccepted);
        var skeleton = Skeleton().Where(n => n.Name != "_weapon_tip").ToList();
        var error = Assert.Throws<Xunit.Sdk.TrueException>(() => FighterItemAdmissionTests.Item(attachments: new[] { "_weapon_tip" },
            model: Build(new[] { Wave("fixture", 12) }, skeleton: skeleton)));
        Assert.Contains("item.pose-bake.point", error.Message);
    }

    [Fact]
    public void TheRuleOneBakeOfTheFixtureIsPinned()
    {
        // Root rule 1 is retained for every published fighter; a different hash needs a new rootRuleVersion.
        var item = FighterItemAdmissionTests.Item();
        string hash = WorkshopItemManifestCodec.HashPayload(item[CharacterPackageAssembler.PosePath]);
        Assert.Equal("1279381495fd8b06345e6f251599cdaf131bb3fae67f2502e736d90e00745728", hash);
    }

    [Fact]
    public void AShiftedTrackIsRefusedAtAdmission()
    {
        var item = FighterItemAdmissionTests.Item();
        var poses = item[CharacterPackageAssembler.PosePath];
        var baked = BakedAnimationData.LoadFromBin(poses);
        string track = baked.Animations[3].Name;
        int frameBytes = baked.BoneNames.Count * 12;
        int at = 16 + baked.BoneNames.Sum(n => 4 + Encoding.UTF8.GetByteCount(n));
        for (int a = 0; a < 3; a++) at += 8 + Encoding.UTF8.GetByteCount(baked.Animations[a].Name) + baked.Animations[a].FrameCount * frameBytes;
        at += 8 + Encoding.UTF8.GetByteCount(track);
        for (int f = 0; f < baked.Animations[3].FrameCount; f++)
            for (int p = 0; p < baked.BoneNames.Count; p++)
            {
                int y = at + f * frameBytes + p * 12 + 4;
                float shifted = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(poses, y)) + .01f;
                BitConverter.GetBytes(BitConverter.SingleToInt32Bits(shifted)).CopyTo(poses, y);
            }
        item["manifest.json"] = CharacterPackageAssembler.RehashManifest(item);
        FighterItemAdmissionTests.RehashItem(item);
        var result = FighterItemAdmissionTests.ReadItem(item);
        Assert.False(result.IsAccepted);
        var refusal = Assert.Single(result.Diagnostics);
        Assert.Equal("item.pose-bake.mismatch", refusal.Code);
        Assert.Contains($"Track '{track}' frame 0", refusal.Message);
        Assert.Contains("10 mm", refusal.Message);
    }

    private static readonly byte[] Library = Build(new[] { Wave("library.wave", 30, loop: true) });
    private static SharedClipRegistry Registry(string sha) => SharedClipRegistry.Parse(Encoding.UTF8.GetBytes(new JsonObject
    {
        ["schemaVersion"] = 1,
        ["clips"] = new JsonArray(new JsonObject { ["id"] = "slop.anim.wave.test.v1", ["version"] = "1.0.0", ["file"] = "library.glb", ["clipName"] = "library.wave", ["sha256"] = sha }),
    }.ToJsonString()), file => file == "library.glb" ? Library : null);
    private static JsonObject Source(string id, bool shared) => id != "anim.idle" ? new JsonObject { ["kind"] = "glb", ["clipName"] = "fixture" }
        : shared ? new JsonObject { ["kind"] = "shared", ["id"] = "slop.anim.wave.test.v1", ["version"] = "1.0.0" } : new JsonObject { ["kind"] = "glb", ["clipName"] = "wave" };

    [Fact]
    public void TheShippedSharedClipTableIsEmpty()
    {
        Assert.False(SharedClipRegistry.Default.Contains("slop.anim.wave.test.v1", "1.0.0"));
        Dictionary<string, byte[]> shared;
        using (SharedClipRegistry.Use(Registry(WorkshopItemManifestCodec.HashPayload(Library))))
            shared = FighterItemAdmissionTests.Item(animation: id => Source(id, true));
        Assert.Contains(FighterItemAdmissionTests.ReadItem(shared).Diagnostics, d => d.Code == "portable.shared.unsupported");
    }

    [Fact]
    public void ARegisteredClipBakesAndPlaysLikeAGlbClip()
    {
        using var scope = SharedClipRegistry.Use(Registry(WorkshopItemManifestCodec.HashPayload(Library)));
        var shared = FighterItemAdmissionTests.Item(animation: id => Source(id, true));
        var own = FighterItemAdmissionTests.Item(animation: id => Source(id, false));
        Assert.Equal(own[CharacterPackageAssembler.PosePath], shared[CharacterPackageAssembler.PosePath]);
        var result = FighterItemAdmissionTests.ReadItem(shared);
        Assert.True(result.IsAccepted, string.Join("\n", result.Diagnostics));
        var motion = result.Item!.Motion;
        var played = motion.Animations.Single(a => a.SemanticId == "anim.idle").Clip;
        var glb = motion.Rig.Clip("wave");
        Assert.True(played.Loop);
        var a = new GlbNodePose[motion.Rig.NodeCount];
        var b = new GlbNodePose[motion.Rig.NodeCount];
        for (int tick = 0; tick < glb.FrameCount; tick++)
        {
            motion.Rig.SampleLocal(played, tick, a);
            motion.Rig.SampleLocal(glb, tick, b);
            Assert.Equal(b, a);
        }
        using (SharedClipRegistry.Use(Registry(new string('0', 64))))
            Assert.Contains(FighterItemAdmissionTests.ReadItem(shared).Diagnostics, d => d.Code == "item.read");
    }
}

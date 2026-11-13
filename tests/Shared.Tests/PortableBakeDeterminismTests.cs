using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;
using static SlopArena.Shared.Tests.TestGlb;

namespace SlopArena.Shared.Tests;

/// <summary>Inputs chosen so that any extra precision, contraction or flush-to-zero would change the baked bytes.</summary>
public sealed class PortableBakeDeterminismTests
{
    private const float Subnormal = 1e-40f;

    /// <summary>
    /// The left hand sits at glTF x = 1 + 2^-24 (an exact binary32 tie once doubled by visualScale 2), a marker sits a
    /// subnormal distance from the hips, two rest values are halfway decimals, and the hips keys are subnormal.
    /// </summary>
    internal static Dictionary<string, byte[]> AdversarialItem()
    {
        var skeleton = Skeleton().Select(n => n.Name switch
        {
            P + "LeftShoulder" => n with { Translation = V(0, .1f, 0) },
            P + "LeftArm" => n with { Translation = V(0, 0, 0) },
            P + "LeftForeArm" => n with { Translation = V(1, 0, 0) },
            P + "LeftHand" => n with { Translation = V(MathF.Pow(2, -24), 0, 0) },
            "_weapon_hilt" => n with { Parent = P + "Hips", Translation = V(Subnormal, 0, 0) },
            _ => n,
        }).ToList();
        var times = Ticks(4);
        var hips = new float[] { Subnormal, 1, 0, -Subnormal, 1, 0, 2 * Subnormal, 1, Subnormal, 0, 1, -Subnormal };
        var still = new Clip("still", 4 / 60f, new[] { new Channel(P + "Hips", "translation", times, hips) });
        var glb = Rewrite(Build(new[] { still }, skeleton: skeleton), (json, _) =>
        {
            foreach (var node in json["nodes"]!.AsArray())
            {
                string name = node!["name"]!.GetValue<string>();
                if (name == P + "Neck") node["translation"] = JsonNode.Parse("[0, 1.000000059604644775390625, 0]");
                if (name == P + "Head") node["translation"] = JsonNode.Parse("[0, 1.00000005960464477539062500000001, 1e-40]");
            }
        });
        string root = Path.Combine(CookedPackageEdits.Root, "tests/Shared.Tests/Fixtures/Tumbleweed");
        var character = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "character.json")))!;
        character["presentation"]!["visualScale"] = 2;
        character["presentation"]!["modelYOffset"] = 0;
        character["attachmentBoneIds"] = new JsonArray("_weapon_hilt");
        var package = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "package.json")))!;
        package["packageId"] = "local.adversarial";
        var compiled = CharacterPackageCompiler.CompilePortable(package.ToJsonString(), character.ToJsonString());
        Assert.False(compiled.HasErrors, string.Join("\n", compiled.Diagnostics));
        var animations = CharacterPackageAssembler.RequiredAnimations(compiled.CookedPackage!, new List<CharacterDiagnostic>())
            .ToDictionary(x => x, _ => new JsonObject { ["kind"] = "glb", ["clipName"] = "still" }, StringComparer.Ordinal);
        var (item, diagnostics) = PortableFighterCook.Cook(new PortableFighterSource(Encoding.UTF8.GetBytes(package.ToJsonString()),
            Encoding.UTF8.GetBytes(character.ToJsonString()), Encoding.UTF8.GetBytes("{\"description\":\"Adversarial bake fixture\",\"workInProgress\":true,\"tutorialHint\":null}"),
            "adversarial.glb", glb, animations, "test", "CC0", "Synthetic rounding fixture", false));
        Assert.True(item != null, string.Join("\n", diagnostics));
        return item!;
    }

    [Fact]
    public void TiesRoundToEvenAndSubnormalsSurviveTheBake()
    {
        var item = AdversarialItem();
        if (Environment.GetEnvironmentVariable("SLOP_ADVERSARIAL_PROOF") is { } output)
        {
            Directory.CreateDirectory(output);
            foreach (var file in item) File.WriteAllBytes(Path.Combine(output, file.Key), file.Value);
        }
        var baked = BakedAnimationData.LoadFromBin(item[CharacterPackageAssembler.PosePath]);
        int idle = baked.FindAnimIndex("anim.idle");
        Assert.True(baked.TryGetPoint(idle, 0, baked.FindBoneIndex("mixamorig:LeftHand"), out float handX, out _, out _));
        Assert.Equal(BitConverter.SingleToInt32Bits(-2f), BitConverter.SingleToInt32Bits(handX));
        Assert.True(baked.TryGetPoint(idle, 0, baked.FindBoneIndex("_weapon_hilt"), out float hiltX, out _, out _));
        Assert.Equal(BitConverter.SingleToInt32Bits((float)(-2.0 * Subnormal)), BitConverter.SingleToInt32Bits(hiltX));
        Assert.True(float.IsSubnormal(hiltX));
        string hash = WorkshopItemManifestCodec.HashPayload(item[CharacterPackageAssembler.PosePath]);
        Assert.Equal("f02841ce276605132564735b50ec023900bbd0bd587a3776db6f9437afcb3477", hash);
        var result = FighterItemAdmissionTests.ReadItem(item);
        Assert.True(result.IsAccepted, string.Join("\n", result.Diagnostics));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static double MultiplyAdd(double a, double b, double c) => a * b + c;

    [Fact]
    public void TheJitDoesNotContractAMultiplyAndAdd()
    {
        double a = 1 + Math.Pow(2, -27);
        Assert.Equal(0.0, MultiplyAdd(a, a, -(1 + Math.Pow(2, -26))));
    }

    /// <summary>Only exactly rounded or integer-valued Math functions may reach the bake; libm-backed ones differ by platform.</summary>
    [Fact]
    public void TheBakeCallsOnlyExactMathFunctions()
    {
        var allowed = new HashSet<string> { "Sqrt", "Floor", "Ceiling", "Abs", "Max", "Min" };
        var types = new[] { typeof(GlbMotionRig), typeof(GlbClip), typeof(GlbRootCorrection), typeof(PortablePoseBake), typeof(PortableFighterMotion),
            typeof(GlbMotionRig).Assembly.GetType("SlopArena.Shared.PortableDecimal")! };
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value, o => o);
        var calls = new List<string>();
        foreach (var type in types.SelectMany(t => t.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).Prepend(t)))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) continue;
                for (int at = 0; at < il.Length;)
                {
                    short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
                    var opcode = opcodes[value];
                    at += opcode.Size;
                    int operand = opcode.OperandType switch
                    {
                        OperandType.InlineNone => 0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at), _ => 4,
                    };
                    if (opcode.OperandType == OperandType.InlineMethod)
                    {
                        var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, at),
                            method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null, method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (called?.DeclaringType == typeof(Math) || called?.DeclaringType == typeof(MathF)) calls.Add(called.DeclaringType.Name + "." + called.Name);
                    }
                    at += operand;
                }
            }
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains(call.Split('.')[1], allowed));
        Assert.DoesNotContain(calls, call => call.StartsWith("MathF.", StringComparison.Ordinal));
    }
}

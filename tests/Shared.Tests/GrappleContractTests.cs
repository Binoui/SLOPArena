using System;
using System.IO;
using System.Linq;
using Xunit;
namespace SlopArena.Shared.Tests;
public class GrappleContractTests
{
    private static CharacterPackageSource Source()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "content-cooked"))) root = root.Parent;
        string dir = Path.Combine(root!.FullName, "client/Unity/Assets/CharacterPackages/bashie");
        var source = CharacterPackageSourceCodec.Load(File.ReadAllText(Path.Combine(dir,"package.json")), File.ReadAllText(Path.Combine(dir,"character.json"))).Source!;
        var c = source.Character;
        var ring = c.Slots.SelectMany(s=>s.Timeline.Stages).SelectMany(s=>s.Operations).OfType<StartCapabilityOperationSource>()
            .Select(o=>o.Parameters).OfType<TetherCapabilityParameters>().Single().Projectile!;
        var kick = new HitboxSource(AuthoringHitboxShape.Sphere, 1, 0, 0, .65f, 0, 0, 0, null, null, 10, 35, 8, 20, 16, 1, true, 0);
        var p = new GrappleCapabilityParameters(8,6,5,.2f,14,.6f,.6f,3,20,24,24,4,12,20,.3f,ring,kick);
        var slots = c.Slots.Select(s=>s.Id != "ground.E" ? s : s with { Behavior = AuthoringAbilityBehavior.MeleeCombo, ShellPolicy = ShellPolicy.PopOut, IsRecoveryMove = true, AimMode = AuthoringAimMode.None,
            Timeline = new CharacterTimelineSource(new[] { s.Timeline.Stages[0] with { IasaTicks=0, Operations = new CharacterTimelineOperationSource[] {
                new StartCapabilityOperationSource(0,AuthoringUnit.Ticks,CharacterPackageCompiler.GrappleCapabilityId,"1",p) } } }) }).ToArray();
        return source with { Character = c with { Slots=slots, CapabilityRequirements=c.CapabilityRequirements.Append(new CapabilityRequirementSource(CharacterPackageCompiler.GrappleCapabilityId,"1")).ToArray() } };
    }
    [Fact]
    public void SourceCookAndLoaderRoundTripWithOptInApi123()
    {
        var source = Source();
        var parsed = CharacterPackageSourceCodec.Load(CharacterPackageSourceCodec.SerializeManifest(source.Manifest), CharacterPackageSourceCodec.SerializeCharacter(source.Character));
        Assert.True(parsed.IsValid, string.Join(";",parsed.Diagnostics));
        var compiled = CharacterPackageCompiler.Compile(parsed.Source!,CharacterCookProfile.TrustedBuiltIn);
        Assert.False(compiled.HasErrors,string.Join(";",compiled.Diagnostics));
        Assert.Equal("1.23.0", compiled.CookedPackage!.Metadata.RuntimeApiMin);
        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(compiled.CookedPackage)));
        Assert.True(loaded.IsValid,string.Join(";",loaded.Diagnostics));
        Assert.Equal(compiled.CookedPackage.CanonicalBytes, loaded.Package!.CanonicalBytes);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void CompilerRejectsWrongSlotShapeOrBounds(int mode)
    {
        var source=Source();
        source=source with { Character=source.Character with { Slots=source.Character.Slots.Select(s=>s.Id!="ground.E" ? s : mode==0 ? s with { IsRecoveryMove=false } : mode==1 ? s with { AimMode=AuthoringAimMode.GroundCursor } : s with {
            Timeline=new CharacterTimelineSource(new[] { s.Timeline.Stages[0] with { Operations=new CharacterTimelineOperationSource[] {
                ((StartCapabilityOperationSource)s.Timeline.Stages[0].Operations[0]) with { Parameters=((GrappleCapabilityParameters)((StartCapabilityOperationSource)s.Timeline.Stages[0].Operations[0]).Parameters) with { HaulTicks=0 } } } } }) }).ToArray() } };
        Assert.True(CharacterPackageCompiler.Compile(source,CharacterCookProfile.TrustedBuiltIn).HasErrors);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void LoaderRejectsOldApiAndMalformedGrapple(bool oldApi)
    {
        var compiled = CharacterPackageCompiler.Compile(Source(), CharacterCookProfile.TrustedBuiltIn).CookedPackage!;
        var bytes = System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(compiled.CanonicalBytes)
            .Replace(oldApi ? "1.23.0" : "\"haulTicks\":14", oldApi ? "1.22.0" : "\"haulTicks\":0"));
        var altered = new CookedCharacterPackage(oldApi ? compiled.Metadata with { RuntimeApiMin="1.22.0" } : compiled.Metadata,
            compiled.Definition, compiled.Budget, compiled.Diagnostics, bytes);
        var loaded = CookedCharacterPackageLoader.LoadAssembly(CharacterPackageAssembler.Assemble(ArmorAuthoringTests.BuildInput(altered)));
        Assert.False(loaded.IsValid);
        Assert.Contains(loaded.Diagnostics, d=> d.Code == (oldApi ? "package.compatibility.unsupported" : "package.capability.parameters-invalid"));
    }

}

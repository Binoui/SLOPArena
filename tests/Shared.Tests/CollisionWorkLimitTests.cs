using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class CollisionWorkLimitTests(ITestOutputHelper output)
{
    [Fact]
    public void ReviewPressurePackageIsRefusedForBodiesAndWeightedCollisionWork()
    {
        foreach(int bodies in new[]{128,32})
        {
            var files=CollisionWorkScenario.PressurePackage(bodies:bodies);
            var load=CookedCharacterPackageLoader.LoadFiles(files,CookedPackageEdits.RequirementOf(files),CharacterCookProfile.Workshop);
            Assert.False(load.IsValid);
            Assert.Contains(load.Diagnostics,d=>d.Code==(bodies==128?"package.hurtboxes.limit":"package.collision.limit"));
        }
    }

    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(4)]
    public void RuntimeCapsHoldEvenForTheRejectedReviewDefinition(int seats)
    {
        var fixture=CollisionWorkScenario.UncheckedRuntimeFixture(CollisionWorkScenario.PressurePackage());
        var report=CollisionWorkScenario.Run(fixture,seats);
        output.WriteLine(report.ToString());
        Assert.InRange(report.PeakVolumes,1,seats*8);
        Assert.InRange(report.PeakChecks,0,seats*(seats-1)*128*8);
        Assert.True(report.Refused>0);
    }

    [Fact]
    public void WorstAdmissibleFourSeatMatchBoundsPeakAndSustainedCollisionWork()
    {
        var files=CollisionWorkScenario.PressurePackage(hitboxes:8,projectiles:0,bodies:CookedPackageLimits.BodyHurtboxes,sweepStress:true);
        var load=CookedCharacterPackageLoader.LoadFiles(files,CookedPackageEdits.RequirementOf(files),CharacterCookProfile.Workshop);
        Assert.True(load.IsValid,string.Join(";",load.Diagnostics));
        var report=CollisionWorkScenario.Run(load,4,sweepStress:true);
        output.WriteLine(report.ToString());
        // Fixed ceilings intentionally force review when tuning admission or runtime constants.
        Assert.InRange(report.PeakVolumes,1,64);
        Assert.InRange(report.MeanVolumes,8,64);
        Assert.InRange(report.PeakPairs,1,6336);
        Assert.InRange(report.PeakChecks,70000,79200);
        Assert.InRange(report.MeanChecks,60000,79200);
    }

    [Theory]
    [InlineData("manki")][InlineData("fightguy")][InlineData("wibou")][InlineData("bonk")]
    [InlineData("bashie")][InlineData("salamander")][InlineData("nyx")]
    public void FourSeatBuiltInMatchExercisesEveryMoveWithoutHittingCaps(string id)
    {
        var report=CollisionWorkScenario.BuiltIn(id);
        output.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
        Assert.Equal(0,report.Refused);
        Assert.True(report.OwnerPeak<CookedPackageLimits.LiveVolumesPerOwner);
        Assert.True(report.WorldPeak<CookedPackageLimits.LiveVolumesPerWorld);
        Assert.True(report.ProjectileOwnerPeak<CookedPackageLimits.ProjectileVolumesPerOwner);
        Assert.True(report.ProjectileWorldPeak<CookedPackageLimits.ProjectileVolumesPerWorld);
        int forms=id=="nyx"?2:1;
        for(int form=0;form<forms;form++)
        foreach(string prefix in new[]{"ground.","air."})
        foreach(string key in new[]{"1","2","3","A","E","R","F"})
            Assert.Contains($"{form}:{prefix}{key}",report.Moves);
    }

    [Fact]
    public void RuntimePoolsAreDerivedFromRestoredVolumesAndRefusalDoesNotConsumeAnEmissionId()
    {
        var resolver=new SpellResolver();
        Hitbox Volume(ulong id,bool melee=false)=>new(){OwnerId=id,FreezesOwner=melee,DurationTicks=100};
        for(int n=0;n<12;n++) resolver.Spawn(Volume(1));
        resolver.Spawn(Volume(1));
        Assert.Equal(1,resolver.RefusedSpawns);
        Assert.Equal(12,resolver.GetActiveHitboxes().Count);
        resolver.Spawn(Volume(1,true));
        Assert.Equal(13UL,resolver.GetActiveHitboxes().Last().EmissionId);
        for(ulong id=2;id<=4;id++) for(int n=0;n<12;n++) resolver.Spawn(Volume(id));
        resolver.Spawn(Volume(5));
        Assert.Equal(2,resolver.RefusedSpawns);
        Assert.Equal(48,resolver.GetActiveHitboxes().Count(v=>!v.FreezesOwner));
        for(ulong id=1;id<=4;id++) for(int n=0;n<(id==1?3:4);n++) resolver.Spawn(Volume(id,true));
        Assert.Equal(64,resolver.GetActiveHitboxes().Count);
        resolver.Spawn(Volume(5,true));Assert.Equal(3,resolver.RefusedSpawns);
        var weighted=new SpellResolver();
        for(int n=0;n<9;n++) weighted.Spawn(new Hitbox {OwnerId=1,TracksBone=true,FreezesOwner=true,DurationTicks=100});
        Assert.Equal(8,weighted.GetActiveHitboxes().Count);Assert.Equal(1,weighted.RefusedSpawns);
        // Restore through the actual world codec; no cap accounting survives outside its live list.
        var sim=TestHelpers.MakeSim();
        sim.Resolver.Spawn(Volume(1));
        var snapshot=sim.CaptureSnapshot();
        for(int n=0;n<12;n++)sim.Resolver.Spawn(Volume(1));
        sim.RestoreSnapshot(snapshot);
        for(int n=0;n<11;n++)sim.Resolver.Spawn(Volume(1));
        Assert.Equal(12,sim.Resolver.GetActiveHitboxes().Count);
    }

    [Fact]
    public void EngineOwnedHazardsKeepTheirOwnBudgetWithoutConsumingFighterPools()
    {
        var resolver=new SpellResolver();
        foreach(ulong owner in new[]{StageHazards.StageHazardOwner.EntityId,ServerSimulation.MayhemHostEntityId})
            for(int n=0;n<40;n++) resolver.Spawn(new Hitbox {OwnerId=owner,DurationTicks=100});
        for(int n=0;n<16;n++)resolver.Spawn(new Hitbox {OwnerId=1,FreezesOwner=true,DurationTicks=100});
        Assert.Equal(0,resolver.RefusedSpawns);Assert.Equal(96,resolver.GetActiveHitboxes().Count);
        resolver.Spawn(new Hitbox {OwnerId=1,FreezesOwner=true,DurationTicks=100});
        Assert.Equal(1,resolver.RefusedSpawns);
    }

    [Theory]
    [InlineData("stream",1)][InlineData("stream",3)][InlineData("guns",1)][InlineData("tether",1)]
    public void CapabilityEmittersRespectOneEmissionPerTickAndRuntimeCaps(string kind,ushort interval)
    {
        CookedCapabilityParameters parameters=kind switch
        {
            "stream"=>ChargedStreamTests.Parameters with {EmitIntervalTicks=interval},
            "guns"=>FingerGunsTests.Parameters,
            _=>TetherTests.Parameters,
        };
        string capability=kind switch {"stream"=>CharacterPackageCompiler.ChargedStreamCapabilityId,"guns"=>CharacterPackageCompiler.FingerGunsCapabilityId,_=>CharacterPackageCompiler.TetherCapabilityId};
        var def=TestHelpers.EngineDef;var slots=def.CookedSlots!.ToArray();
        int ordinal=Array.FindIndex(slots,s=>s.Id=="ground.A");
        slots[ordinal]=new CookedSlotDefinition(ordinal,"ground.A",false,"Emitter","","",AuthoringAbilityBehavior.MeleeCombo,AuthoringAimMode.None,0,false,false,
            new CookedTimeline(new[]{new CookedStage(2,0,0,0,0,Array.Empty<string>(),new CookedTimelineOperation[]{new CookedStartCapabilityOperation(0,AuthoringUnit.Ticks,capability,"1",parameters)})}));
        def=def with {CookedSlots=slots};
        var sim=TestHelpers.MakeSim();sim.RegisterEntity(1,def,TestHelpers.PlayerState(20,20));
        // Fill the projectile pool without hits or expiry, then run the real emitter.
        for(int n=0;n<12;n++)sim.Resolver.Spawn(new Hitbox {OwnerId=1,X=-30,Y=20,Z=-30,DurationTicks=1000});
        long attempted=sim.Resolver.SpawnAttempts;
        for(int t=0;t<300;t++)
        {
            long before=sim.Resolver.SpawnAttempts;
            sim.Tick(new Dictionary<ulong,InputState>{[1]=new(){ActiveSlot=AbilitySlots.A,IsAiming=t%90<60}});
            Assert.InRange(sim.Resolver.SpawnAttempts-before,0,1);
            Assert.True(sim.Resolver.GetActiveHitboxes().Count<=12);
        }
        Assert.True(sim.Resolver.SpawnAttempts>attempted);
        Assert.True(sim.Resolver.RefusedSpawns>0);
    }

    [Fact]
    public void FormSwapMidActivationCannotResetRuntimePoolsAndReplaysIdentically()
    {
        var sim=FormsFixture.Sim(FormsFixture.Def(),dummy:false);
        ulong owner=sim.GetAllStates().Keys.Single();
        for(int n=0;n<12;n++)sim.Resolver.Spawn(new Hitbox {OwnerId=owner,X=-30,Y=20,Z=-30,DurationTicks=1000});
        FormsFixture.Step(sim,FormsFixture.Press(AbilitySlots.F));
        var snapshot=sim.CaptureSnapshot();
        var restored=FormsFixture.Sim(FormsFixture.Def(),dummy:false);restored.RestoreSnapshot(snapshot);
        for(int t=0;t<40;t++)
        {
            FormsFixture.Step(sim);FormsFixture.Step(restored);
            Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
            Assert.InRange(sim.Resolver.GetActiveHitboxes().Count,12,16);
        }
        Assert.Equal(1,sim.GetState(owner).Form.Active);
        sim.Resolver.Spawn(new Hitbox {OwnerId=owner,DurationTicks=1000});
        restored.Resolver.Spawn(new Hitbox {OwnerId=owner,DurationTicks=1000});
        Assert.True(sim.Resolver.RefusedSpawns>0);Assert.True(restored.Resolver.RefusedSpawns>0);
        Assert.True(sim.CaptureSnapshot().ContentEquals(restored.CaptureSnapshot()));
    }

    [Fact]
    public void SchemaProjectileLimitsTrackAdmissionConstants()
    {
        var schema=CharacterSourceSchema.Object("projectile");
        Assert.Equal(CookedPackageLimits.ProjectileSpeed,schema.Field("speed").GameplayMaximum);
        Assert.Equal(CookedPackageLimits.ProjectileLifetime,schema.Field("maxFlightTicks").GameplayMaximum);
    }

    [Theory]
    [InlineData("speed",121)][InlineData("maxFlightTicks",961)]
    public void CookerAndLoaderUseTheSameProjectileDiagnostic(string field,int value)
    {
        string root=Path.Combine(CookedPackageEdits.Root,"client/Unity/Assets/CharacterPackages/bonk");
        var source=JsonNode.Parse(File.ReadAllText(root+"/character.json"))!;
        var projectile=source["slots"]!.AsArray().SelectMany(s=>s!["timeline"]!["stages"]!.AsArray())
            .SelectMany(s=>s!["operations"]!.AsArray()).First(o=>o!["projectile"]!=null)!["projectile"]!;
        projectile[field]=value;
        var cook=CharacterPackageCompiler.Compile(File.ReadAllText(root+"/package.json"),source.ToJsonString(),CharacterCookProfile.TrustedBuiltIn);
        Assert.Contains(cook.Diagnostics,d=>d.Code=="package.projectile.limit");
        var files=CookedPackageEdits.Edit("bonk",p=>p.Slot("ground.A")["timeline"]!["stages"]![0]!["operations"]!.AsArray().First(o=>o!["projectile"]!=null)!["projectile"]![field]=value);
        var load=CookedCharacterPackageLoader.LoadFiles(files,CookedPackageEdits.RequirementOf(files),CharacterCookProfile.Workshop);
        Assert.Contains(load.Diagnostics,d=>d.Code=="package.projectile.limit");
    }

    [Fact]
    public void CookTrustRequiresRosterMembershipAndTheApplicationsPhysicalFirstPartyFolder()
    {
        var roster=ShippedRoster.ReadApplication();
        string project=Path.GetFullPath(Path.Combine(roster.CookedRoot,"../client/Unity"));
        Assert.True(roster.AuthorizesCookSource(project,"Assets/CharacterPackages/nyx","nyx"));
        Assert.False(roster.AuthorizesCookSource(project,"Assets/Other/nyx","nyx"));
        Assert.False(roster.AuthorizesCookSource(Path.GetTempPath(),"Assets/CharacterPackages/nyx","nyx"));
        Assert.False(roster.AuthorizesCookSource(project,"Assets/CharacterPackages/newcomer","newcomer"));
    }
}

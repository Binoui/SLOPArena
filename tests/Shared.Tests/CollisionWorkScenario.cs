using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SlopArena.Shared.Tests;

public static class CollisionWorkScenario
{
    public static Dictionary<string, byte[]> PressurePackage(int hitboxes = 48, int projectiles = 16, int bodies = 128, int hitLife = 42, int flight = 285, bool sweepStress = false)
    {
        return CookedPackageEdits.Edit("fightguy", p =>
        {
            var c = p.Runtime["character"]!.AsObject();
            var sample = c["slots"]!.AsArray().SelectMany(s => s!["timeline"]!["stages"]!.AsArray())
                .SelectMany(s => s!["operations"]!.AsArray()).First(o => (int)o!["kind"]! == 1)!.DeepClone();
            var proj = JsonNode.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(CookedPackageEdits.Root,"content-cooked/bonk/character.runtime.json")))!["character"]!["slots"]!.AsArray()
                .SelectMany(s => s!["timeline"]!["stages"]!.AsArray()).SelectMany(s => s!["operations"]!.AsArray()).First(o => (int)o!["kind"]! == 2)!.DeepClone();
            p.Runtime["metadata"]!["compatibility"]!["runtimeApiMin"] = "1.22.0";
            p.Manifest["runtimeApiMin"] = "1.22.0";
            c["capabilityRequirements"] = new JsonArray();
            p.Manifest["capabilityRequirements"] = new JsonArray();
            foreach (var slot in c["slots"]!.AsArray())
            foreach (var stage in slot!["timeline"]!["stages"]!.AsArray()) stage!["operations"] = new JsonArray();
            if(sweepStress) {sample["hitbox"]!["offsetX"]=100;sample["hitbox"]!["offsetY"]=100;}
            sample["tick"] = 0; sample["hitbox"]!["durationTicks"] = hitLife; sample["hitbox"]!["radius"] = 2;
            proj["tick"] = 0; proj["projectile"]!["maxFlightTicks"] = flight; proj["projectile"]!["speed"] = 2;
            proj["projectile"]!.AsObject().Remove("return");
            var operations = new JsonArray();
            for (int n=0;n<hitboxes;n++) operations.Add(sample.DeepClone());
            for (int n=0;n<projectiles;n++) operations.Add(proj.DeepClone());
            var first = p.Slot("ground.1"); first["cooldownTicks"] = 0;
            var one = first["timeline"]!["stages"]![0]!.DeepClone(); one["durationTicks"] = sweepStress?43:1; one["iasaTicks"] = 0; one["operations"] = operations;
            first["timeline"]!["stages"] = new JsonArray(one);
            var boneBodies = c["hurtboxBoneDefs"]!.AsArray(); var body = boneBodies[0]!.DeepClone();
            while (boneBodies.Count > bodies) boneBodies.RemoveAt(boneBodies.Count-1);
            while (boneBodies.Count < bodies) boneBodies.Add(body.DeepClone());
            c["hurtboxCapsules"] = new JsonArray();
            int stages=0, duration=0;
            foreach(var slot in c["slots"]!.AsArray()) { int ticks=0; foreach(var stage in slot!["timeline"]!["stages"]!.AsArray()) { stages++;ticks+=(int)stage!["durationTicks"]!; } duration=Math.Max(duration,ticks); }
            p.Runtime["budget"] = new JsonObject { ["slotCount"]=14,["stageCount"]=stages,["operationCount"]=hitboxes+projectiles,["hitboxCount"]=hitboxes,["projectileCount"]=projectiles,["capabilityCount"]=0,["maxTimelineDurationTicks"]=duration };
        });
    }

    // Benchmark-only: construct an invalid runtime fixture without adding a production admission bypass.
    public static CookedCharacterPackageLoadResult UncheckedRuntimeFixture(Dictionary<string, byte[]> files)
    {
        var parser=typeof(CookedCharacterPackageLoader).GetNestedType("RuntimeParser",System.Reflection.BindingFlags.NonPublic)!;
        var package=(CookedCharacterPackage)parser.GetMethod("Parse")!.Invoke(null,new object[]{files[CharacterPackageAssembler.RuntimePath]})!;
        var original=CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped("fightguy"),CookedPackageEdits.Roster.ResolvePackage("fightguy")!.Requirement,CharacterCookProfile.TrustedBuiltIn);
        return new CookedCharacterPackageLoadResult(package,original.BakedAnimation,original.Identity,Array.Empty<CharacterDiagnostic>());
    }

    public sealed record Report(int Seats, int Ticks, int PeakVolumes, long PeakChecks, double MeanChecks, double MedianMs, double WorstMs, long PeakPairs, double MeanVolumes, long Refused);
    public static Report Run(CookedCharacterPackageLoadResult pressure, int seats, int ticks=1200, bool everySeat=true, bool sweepStress=false)
    {
        if(!pressure.IsValid) throw new Exception(string.Join(";",pressure.Diagnostics));
        var other = CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped("fightguy"),CookedPackageEdits.Roster.ResolvePackage("fightguy")!.Requirement);
        var arena = ArenaBinaryFormat.LoadFromFile(Path.Combine(CookedPackageEdits.Root,"data/arenas/slop_pit.arena"))!.Value;
        var sim=new ServerSimulation(arena,new StockMatchRule(250));
        for(ulong id=1;id<=(ulong)seats;id++) {
            var loaded = everySeat || id==1 ? pressure:other;
            var def=loaded.ToCharacterDefinition();
            sim.RegisterEntity(id,def,new CharacterState {EntityId=id,PX=(id%2==0?4:-4),PY=1,PZ=(id>2?4:-4),IsGrounded=true,JumpsLeft=2},loaded.BakedAnimation);
        }
        var times=new double[ticks];int peak=0;long checks=0,sum=0,pairs=0,volumes=0;
        var inputs=new Dictionary<ulong,InputState>();
        for(int t=0;t<ticks;t++) {
            for(ulong id=1;id<=(ulong)seats;id++) {
                var state=sim.GetState(id);state.PX=(id%2==0?4:-4);state.PY=1;state.PZ=(id>2?4:-4);state.VX=state.VY=state.VZ=0;state.IsGrounded=true;state.HitstopTicks=0;state.InvincibilityTicks=0;if(sweepStress)state.FacingYaw=t%2==0?0:MathF.PI;sim.SetState(id,state);
                inputs[id]=new InputState {ActiveSlot=everySeat||id==1?AbilitySlots.Normal1:(byte)0,AimYaw=(short)(sweepStress&&t%2!=0?18000:0)};
            }
            var watch=Stopwatch.StartNew();sim.Tick(inputs);times[t]=watch.Elapsed.TotalMilliseconds;
            peak=Math.Max(peak,sim.Resolver.GetActiveHitboxes().Count);checks=Math.Max(checks,sim.Resolver.CollisionChecksLastTick);sum+=sim.Resolver.CollisionChecksLastTick;pairs=Math.Max(pairs,sim.Resolver.VolumeHurtboxPairsLastTick);volumes+=sim.Resolver.GetActiveHitboxes().Count;
        }
        Array.Sort(times);return new(seats,ticks,peak,checks,(double)sum/ticks,times[ticks/2],times[^1],pairs,(double)volumes/ticks,sim.Resolver.RefusedSpawns);
    }
    public sealed record BuiltInReport(string Package, int OwnerPeak, int WorldPeak, int ProjectileOwnerPeak,
        int ProjectileWorldPeak, long Checks, string[] Moves, long Refused, int OwnerPoseUnits, int WorldPoseUnits);
    public static BuiltInReport BuiltIn(string packageId)
    {
        var load=CookedCharacterPackageLoader.LoadFiles(CookedPackageEdits.Shipped(packageId),CookedPackageEdits.Roster.ResolvePackage(packageId)!.Requirement,CharacterCookProfile.TrustedBuiltIn);
        if(!load.IsValid) throw new Exception(string.Join(";",load.Diagnostics));
        var def=load.ToCharacterDefinition();
        var arena=ArenaBinaryFormat.LoadFromFile(Path.Combine(CookedPackageEdits.Root,"data/arenas/slop_pit.arena"))!.Value;
        var sim=new ServerSimulation(arena,new StockMatchRule(250));
        for(ulong id=1;id<=4;id++) sim.RegisterEntity(id,def,new CharacterState {EntityId=id,PY=1,IsGrounded=true,JumpsLeft=2},load.BakedAnimation);
        byte[] buttons={AbilitySlots.Normal1,AbilitySlots.Normal2,AbilitySlots.Normal3,AbilitySlots.A,AbilitySlots.E,AbilitySlots.R,AbilitySlots.F};
        int owner=0,world=0,po=0,pw=0;long checks=0;var moves=new HashSet<string>();
        int forms=load.Package!.Definition.AlternateForm==null?1:2;
        for(int phase=0;phase<14*forms;phase++)
        {
            // End the previous exercise through the normal hitstun cancellation path.
            for(ulong id=1;id<=4;id++) sim.SetState(id,sim.GetState(id) with {State=ActionState.Hitstun,HitstunTicks=2});
            sim.Tick(new Dictionary<ulong,InputState>());
            for(ulong id=1;id<=4;id++)
            {
                var fresh=new CharacterState {EntityId=id,PY=1,IsGrounded=true,JumpsLeft=2,Form=new FormState {Active=(byte)(phase/14)}};
                Flying.Refuel(ref fresh,def);sim.SetState(id,fresh);
            }
        for(int t=0;t<720;t++)
        {
            var inputs=new Dictionary<ulong,InputState>();
            for(ulong id=1;id<=4;id++)
            {
                var state=sim.GetState(id);state.PX=id%2==0?4:-4;state.PZ=id>2?4:-4;state.PY=phase%14<7?1:4;
                state.VX=state.VY=state.VZ=0;state.IsGrounded=phase%14<7;state.JumpsLeft=2;state.HitstopTicks=0;state.InvincibilityTicks=0;
                
                sim.SetState(id,state);
                inputs[id]=new InputState {ActiveSlot=buttons[phase%7],IsAiming=t%180<120,AimDistance=800,TargetEntityId=(byte)(id%4+1),MoveY=1};
            }
            ulong before=sim.GetLastActivationId(1);
            sim.Tick(inputs);
            if(sim.GetLastActivationId(1)!=before) moves.Add($"{phase/14}:{(phase%14<7?"ground":"air")}.{new[]{"1","2","3","A","E","R","F"}[phase%7]}");
            for(ulong id=1;id<=4;id++) if(sim.GetActiveAbility(id)?.Move is {} move) moves.Add($"{phase/14}:{move.Id}");
            var volumes=sim.Resolver.GetActiveHitboxes();world=Math.Max(world,volumes.Count);
            pw=Math.Max(pw,volumes.Count(v=>!v.FreezesOwner));
            for(ulong id=1;id<=4;id++) { owner=Math.Max(owner,volumes.Count(v=>v.OwnerId==id));po=Math.Max(po,volumes.Count(v=>v.OwnerId==id&&!v.FreezesOwner)); }
            checks=Math.Max(checks,sim.Resolver.CollisionChecksLastTick);
        }
        }
        return new(packageId,sim.Resolver.PeakOwnedVolumes,sim.Resolver.PeakLiveVolumes,sim.Resolver.PeakOwnedProjectileVolumes,sim.Resolver.PeakProjectileVolumes,checks,moves.OrderBy(x=>x).ToArray(),sim.Resolver.RefusedSpawns,sim.Resolver.PeakOwnedCollisionPoseUnits,sim.Resolver.PeakCollisionPoseUnits);
    }

}

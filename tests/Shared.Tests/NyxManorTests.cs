using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace SlopArena.Shared.Tests;

public sealed class NyxManorTests(ITestOutputHelper output)
{
    // Each branch rejoins the next shared approach; no target asks for the opposite branch.
    public static RouteStep[] Route(float side, bool quick, bool low)
    {
        var steps = new List<RouteStep>
        {
            new Go(0,-41), new Break("yard_1"), new Go(0,-37.5f), new Break("yard_2"),
            new Go(0,-34), new Break("giant_bat"), new Go(0,-29.5f), new Break("yard_3"),
        };
        if (quick) steps.AddRange(new RouteStep[]
        {
            new Go(4.2f*side,-21), new Hop(4.2f*side,-18.8f,1.8f), new Hop(4.2f*side,-16.5f,3.6f),
            new Go(14.8f*side,-16), new Go(14.8f*side,-13), new Hop(14.8f*side,-10.8f,5.4f), new Hop(14.8f*side,-8.5f,7.2f),
            new Go(3.2f*side,-8), new Go(2.8f*side,-8), new Break("flamingo"), new Go(3.2f*side,-5),
            new Hop(3.2f*side,-2.8f,9), new Hop(3.2f*side,-.5f,10.8f),
            new Go(14*side,0), new Go(14*side,3), new Hop(14*side,5.2f,12.6f), new Hop(14*side,7.5f,14.4f),
        });
        else steps.AddRange(new RouteStep[]
        {
            new Go(13*side,-27), new Go(13*side,-18.6f), new Go(13*side,-16),
            new Go(13.2f*side,-12), new Go(3.2f*side,-12), new Go(3.2f*side,-8), new Go(2.8f*side,-8), new Break("flamingo"),
            new Go(15.8f*side,-8), new Go(15.8f*side,-4), new Go(7.2f*side,-4), new Go(7.2f*side,0),
            new Go(11.2f*side,0), new Go(11.2f*side,4), new Go(2.2f*side,4), new Go(2.2f*side,7),
        });
        steps.AddRange(new RouteStep[]
        {
            new Go(5*side,8), new Go(5*side,16), new Go(0,16),
            new Break("roof_1"), new Break("cardboard_nyxxie"), new Break("roof_2"),
            new Hop(0,19.5f,10.8f), new Break("goth_balcony_1"),
            new Go(0,20.4f), new Break("goth_gargoyle"), new Break("goth_balcony_2"), new Go(0,20),
        });
        steps.AddRange(low ? new RouteStep[]
        {
            new Go(8,20), new Hop(8,25,4.5f), new Hop(11,30,4.7f), new Hop(7,35,4.9f),
            new Hop(10,39.9f,5), new Go(10,41), new Go(10,48.8f), new Go(10,50), new Go(0,50),
        } : new RouteStep[]
        {
            new Hop(0,26,9), new Hop(0,32,9), new Go(0,35), new Hop(0,40,9), new Go(0,43), new Hop(0,47,9), new Go(0,50),
        });
        steps.AddRange(new RouteStep[]
        {
            new Break("goth_gate"), new Go(0,50.5f), new Break("goth_grave_1"), new Break("goth_grave_2"),
            new Hop(0,54.5f,9), new Hop(0,59.5f,9), new Break("goth_grave_3"), new Break("goth_grave_4"), new Break("goth_grave_5"), new Go(0,61),
        });
        return steps.ToArray();
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void EveryTargetIsInOrdinaryAttackReachOfEveryWay(CharacterClass fighter)
    {
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(CourseFinishability.Root,
            "tools/blender/nyx_manor/target_ways.json")));
        var arena = new CourseFinishability("nyx_manor", fighter).Arena;
        var targets = arena.Arcade!.Course!.Targets;
        var checkedIds = new HashSet<string>();
        foreach (var section in contract.RootElement.GetProperty("sections").EnumerateArray())
        foreach (var id in section.GetProperty("targets").EnumerateArray())
        {
            string name = id.GetString()!;
            checkedIds.Add(name);
            int index = targets.ToList().FindIndex(t => t.Id == name);
            Assert.True(index >= 0, $"Missing target {name}");
            var target = targets[index];
            var centre = new System.Numerics.Vector3(target.Position.X,target.Position.Y,target.Position.Z);
            foreach (var way in section.GetProperty("ways").EnumerateArray())
            {
                var points = way.GetProperty("points").EnumerateArray().Select(p =>
                    new System.Numerics.Vector3(p[0].GetSingle(),p[1].GetSingle()+1,p[2].GetSingle())).ToArray();
                var closest = points[0]; float distance = float.MaxValue;
                for (int i = 1; i < points.Length; i++)
                {
                    var delta = points[i]-points[i-1];
                    float t = Math.Clamp(System.Numerics.Vector3.Dot(centre-points[i-1],delta)/delta.LengthSquared(),0,1);
                    var candidate = points[i-1]+delta*t;
                    float d = System.Numerics.Vector3.Distance(centre,candidate);
                    if (d < distance) { distance=d; closest=candidate; }
                }
                Assert.True(distance <= target.Radius+contract.RootElement.GetProperty("ordinaryReach").GetSingle()+.001f,
                    $"{name} is {distance:F2}m from {way.GetProperty("name").GetString()}");
                bool broken = false;
                foreach (byte slot in new[] { AbilitySlots.Normal1,AbilitySlots.Normal2,AbilitySlots.Normal3 })
                {
                    var d = new CourseFinishability("nyx_manor",fighter);
                    d.Calibrate(closest.X,closest.Y-1,closest.Z);
                    short yaw = (short)MathF.Round(MathF.Atan2(centre.X-closest.X,centre.Z-closest.Z)*18000/MathF.PI);
                    for (int tick=0; tick<180*target.Hits && !d.Sim.IsCourseTargetBroken(index); tick++)
                        d.Sim.Tick(new() { [1]=new InputState { FacingYaw=yaw,AimYaw=yaw,
                            ActiveSlot=tick%180==4 ? slot : (byte)0 } });
                    if (d.Sim.IsCourseTargetBroken(index)) { broken=true; break; }
                }
                Assert.True(broken, $"{fighter} cannot break {name} from {way.GetProperty("name").GetString()} with stationary normals");
            }
        }
        Assert.Equal(targets.Count,checkedIds.Count);
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void EverySafeAndQuickClimbWorksFromAStandingStart(CharacterClass fighter)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            Check(13 * side, 0, -27, new Go(13 * side, -18.6f), new Go(13 * side, -16));
            Check(13.2f * side, 3.6f, -12, new Go(3.2f * side, -12), new Go(3.2f * side, -8));
            Check(15.8f * side, 7.2f, -4, new Go(7.2f * side, -4), new Go(7.2f * side, 0));
            Check(11.2f * side, 10.8f, 4, new Go(2.2f * side, 4), new Go(2.2f * side, 7));
            Check(4.2f * side, 0, -21, new Hop(4.2f * side, -18.8f, 1.8f), new Hop(4.2f * side, -16.5f, 3.6f));
            Check(14.8f * side, 3.6f, -13, new Hop(14.8f * side, -10.8f, 5.4f), new Hop(14.8f * side, -8.5f, 7.2f));
            Check(3.2f * side, 7.2f, -5, new Hop(3.2f * side, -2.8f, 9), new Hop(3.2f * side, -.5f, 10.8f));
            Check(14 * side, 10.8f, 3, new Hop(14 * side, 5.2f, 12.6f), new Hop(14 * side, 7.5f, 14.4f));
        }
        new CourseFinishability("nyx_manor",fighter).Calibrate(13.5f,9,54,new Hop(13.5f,58.5f,11.4f),new Hop(13.5f,63,9));
        new CourseFinishability("nyx_manor",fighter).Calibrate(-13.5f,9,58,new Hop(-13.5f,62.5f,11.4f),new Hop(-13.5f,66.5f,9));
        void Check(float x, float y, float z, params RouteStep[] steps)
        {
            new CourseFinishability("nyx_manor", fighter).Calibrate(x, y, z, steps);
            output.WriteLine($"{fighter} leg {x},{y},{z} PASS");
        }
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void FrontKnockoffsLandBelowAndTheRavineKills(CharacterClass fighter)
    {
        foreach (var (z, y, landing) in new[] { (-18.7f, 3.6f, 0f), (-10.7f, 7.2f, 3.6f), (-2.7f, 10.8f, 7.2f) })
        {
            var d = new CourseFinishability("nyx_manor", fighter);
            var s = d.State; s.PX = 0; s.PY = y + d.Fighter.CapsuleHeight * .5f; s.PZ = z;
            s.IsGrounded = false;
            Simulation.ApplyKnockbackForce(ref s,0,-1,0,6,1);
            s.HitstunTicks = 20; d.Sim.SetState(1, s);
            for (int i = 0; i < 180 && !d.State.IsGrounded; i++) d.Sim.Tick(new());
            Assert.Equal((byte)0, d.State.Deaths);
            Assert.True(d.State.IsGrounded);
            Assert.InRange(d.State.PY - d.Fighter.CapsuleHeight * .5f, landing - .05f, landing + .05f);
        }
        foreach (var (x, z, y) in new[] { (0f,29.25f,9f), (0f,37.25f,9f), (0f,45f,9f), (8f,27.5f,5f), (11f,32.5f,5f), (7f,37.5f,5f), (22f,20f,14.4f) })
        {
            var d = new CourseFinishability("nyx_manor", fighter);
            var s = d.State; s.PX = x; s.PY = y; s.PZ = z; s.IsGrounded = false; d.Sim.SetState(1,s);
            for (int i = 0; i < 300 && d.State.Deaths == 0; i++) d.Sim.Tick(new());
            Assert.Equal((byte)1,d.State.Deaths);
        }
    }

    [Fact]
    public void BakedMarkersKeepTheLockedObjectiveContract()
    {
        var d = new CourseFinishability("nyx_manor",CharacterClass.FightGuy);
        var profile = d.Arena.Arcade!;
        Assert.Equal("nyx_manor_v1",profile.ProfileId);
        Assert.Empty(profile.AllowedPackageIds);
        var course = profile.Course!;
        Assert.Equal(17,course.Targets.Count);
        Assert.Equal(4,course.Targets.Count(t => t.Hits == 5));
        Assert.Equal(9,course.Targets.Count(t => t.Id.StartsWith("goth_")));
        Assert.Equal(new ushort[] { 4,5,5,11 },course.Checkpoints.Select(c => c.RequiredTargetCount));
        Assert.Equal(-12,d.Arena.KillHeight);
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void RacingPathFinishesAfterRivalsClearTargets(CharacterClass fighter)
    {
        var route = CourseFinishability.Routes.Single(r => r.ArenaKey == "nyx_manor");
        var driver = new CourseFinishability("nyx_manor", fighter, output.WriteLine);
        driver.Run(route with { Steps = route.RacingSteps!, Format = CourseFormat.Scramble });
        Assert.All(driver.Inputs, i => Assert.Equal((byte)0,i.ActiveSlot));
    }


    private static CourseRoute LowRoute(CourseRoute high)
    {
        return high with { Steps = Route(-1, quick: false, low: true) };
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void BothCrossingsFinishWithFourCheckpointsAndNoSpecials(CharacterClass fighter)
    {
        var high = CourseFinishability.Routes.Single(r => r.ArenaKey == "nyx_manor");
        foreach (float side in new[] { -1f,1f })
        foreach (bool quick in new[] { false,true })
        foreach (bool low in new[] { false,true })
        {
            var route = high with { Steps=Route(side,quick,low) };
            var d = new CourseFinishability("nyx_manor",fighter,output.WriteLine);
            d.Run(route);
            Assert.Empty(d.SpecialTargets);
            Assert.Equal(new[] { 0,1,2,3 },d.Events.Where(e => e.Kind == CharacterCourseEventKind.CheckpointSaved).Select(e => (int)e.Index));
            Assert.All(d.Inputs, i => Assert.Contains(i.ActiveSlot,new byte[] { 0,AbilitySlots.Normal1,AbilitySlots.Normal2,AbilitySlots.Normal3 }));
            output.WriteLine($"CROSSING {fighter}/side={side}/quick={quick}/low={low} PASS inputs={d.Inputs.Count} courseTicks={d.Sim.CourseFor(1)!.Value.ElapsedTicks} targets=17 checkpoints=4 falls={d.State.Deaths}");
        }
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void BothMausoleumWaysKeepAllTargetsOnTheirApproachOrLanding(CharacterClass fighter)
    {
        var baseRoute = CourseFinishability.Routes.Single(r => r.ArenaKey == "nyx_manor");
        var prefix = baseRoute.Steps.TakeWhile(s => s != new Hop(0,54.5f,9)).ToArray();
        foreach (bool east in new[] { false,true })
        {
            RouteStep[] end = east ? new RouteStep[]
            {
                new Hop(0,54.5f,9), new Go(13.5f,54), new Hop(13.5f,58.5f,11.4f),
                new Hop(13.5f,63,9), new Go(5,63), new Go(5,59.7f), new Go(0,59.7f),
                new Break("goth_grave_3"), new Break("goth_grave_4"), new Break("goth_grave_5"), new Go(0,61),
            } : new RouteStep[]
            {
                new Hop(0,54.5f,9), new Hop(0,59.5f,9), new Break("goth_grave_3"),
                new Break("goth_grave_4"), new Break("goth_grave_5"), new Go(-13.5f,59),
                new Hop(-13.5f,62.5f,11.4f), new Hop(-13.5f,66.5f,9),
                new Go(-5,66.5f), new Go(-5,59.7f), new Go(0,59.7f), new Go(0,61),
            };
            var d = new CourseFinishability("nyx_manor",fighter,output.WriteLine);
            d.Run(baseRoute with { Steps=prefix.Concat(end).ToArray() });
            Assert.Empty(d.SpecialTargets);
            output.WriteLine($"MAUSOLEUM {fighter}/east={east} PASS inputs={d.Inputs.Count} courseTicks={d.Sim.CourseFor(1)!.Value.ElapsedTicks} targets=17 falls=0");
        }
    }

    [Theory]
    [MemberData(nameof(RosterFighters.All), MemberType = typeof(RosterFighters))]
    public void RoofAndBalconyDropsCannotReachTheCryptWithoutCrossing(CharacterClass fighter)
    {
        foreach (float x in new[] { -16f,-8f,0f,8f,16f })
        foreach (var (z,y) in new[] { (18.7f,14.4f),(22.7f,10.8f) })
        {
            var d = new CourseFinishability("nyx_manor",fighter);
            var s=d.State; s.PX=x; s.PY=y+d.Fighter.CapsuleHeight*.5f; s.PZ=z;
            s.IsGrounded=false; d.Sim.SetState(1,s);
            for (int tick=0;tick<300 && d.State.Deaths==0;tick++)
            {
                d.Sim.Tick(new() { [1]=new InputState { MoveY=1 } });
                Assert.True(d.State.PZ<60,"A drop reached the finish without landing on a crossing.");
            }
            Assert.Equal((byte)1,d.State.Deaths);
            Assert.DoesNotContain(d.Sim.LastTickCourseEvents,e=>e.Kind==CharacterCourseEventKind.CheckpointSaved && e.Index==3);
        }
    }

    [Theory]
    [InlineData(CharacterClass.FightGuy, "fightguy")]
    [InlineData(CharacterClass.Nyx, "nyx")]
    public void OrdinaryRouteExportsReplayWhenRequested(CharacterClass fighter, string package)
    {
        if (Environment.GetEnvironmentVariable("SLOPARENA_EXPORT_COURSE_INPUTS") is not { } directory) return;
        var d = new CourseFinishability("nyx_manor",fighter,output.WriteLine);
        var route = CourseFinishability.Routes.Single(r => r.ArenaKey == "nyx_manor");
        d.Run(package == "fightguy" ? LowRoute(route) : route);
        Assert.Empty(d.SpecialTargets);
        Assert.Equal(new[] { 0,1,2,3 },d.Events.Where(e => e.Kind == CharacterCourseEventKind.CheckpointSaved).Select(e => (int)e.Index));
        Directory.CreateDirectory(directory);
        const int settle = 2;
        File.WriteAllText(Path.Combine(directory,$"nyx-manor-{package}.frames.json"),JsonSerializer.Serialize(d.FrameStates.Select((f,i) => new { tick=i+3,f.PX,f.PY,f.PZ,f.VX,f.VY,f.VZ,f.FacingYaw,f.State,f.AttackSlot })));
        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        File.WriteAllText(Path.Combine(directory,$"nyx-manor-{package}.inputs.json"),JsonSerializer.Serialize(
            Enumerable.Repeat(default(InputState),settle).Concat(d.Inputs)
                .Concat(Enumerable.Repeat(new InputState { MoveY = 1 },24)).Select(i => new {
                ticks=1, moveX=i.MoveX, moveY=i.MoveY, jumpPressed=i.Jump, jumpHeld=i.JumpHeld,
                shieldPressed=i.ShieldPressed, shieldHeld=i.ShieldHeld, downPressed=i.DownPressed, downHeld=i.Down,
                facingYaw=i.FacingYaw, aimYaw=i.AimYaw, aimDistance=i.AimDistance, aimPitch=i.AimPitch, isAiming=i.IsAiming,
                slot=i.ActiveSlot switch { 0 => null, AbilitySlots.Normal1 => "1", AbilitySlots.Normal2 => "3", AbilitySlots.Normal3 => "4", _ => throw new InvalidOperationException("Expected a normal attack.") }, targetEntityId=i.TargetEntityId.ToString()
            }),options));
        int At(RouteStep step) => settle+d.Landings.First(l => l.Step == step).InputCount;
        var captures = new[] {
            new { tick=60,name="start",camera=new {yaw=0,pitch=0}},
            new { tick=At(new Go(-3.2f,-8)),name="scaffolding",camera=new {yaw=90,pitch=35}},
            new { tick=At(new Go(0,16)),name="roof",camera=new {yaw=90,pitch=35}},
            new { tick=At(new Hop(0,19.5f,10.8f)),name="balcony",camera=new {yaw=90,pitch=35}},
            new { tick=At(package == "fightguy" ? new Hop(11,30,4.7f) : new Hop(0,32,9)),name="crossing",camera=new {yaw=45,pitch=35}},
            new { tick=At(new Go(0,50)),name="graveyard",camera=new {yaw=0,pitch=35}},
            new { tick=settle+d.Inputs.Count,name="finish",camera=new {yaw=0,pitch=25}},
        };
        File.WriteAllText(Path.Combine(directory,$"nyx-manor-{package}.json"),JsonSerializer.Serialize(new {
            version=1,stage="nyx_manor",fighter=package,inputs=$"nyx-manor-{package}.inputs.json",captures,
            expect=new { completed=true,targets=17,falls=0,checkpoints=4 }
        },options));
    }
}

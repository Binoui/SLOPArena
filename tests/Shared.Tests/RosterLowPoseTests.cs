using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SlopArena.Shared.Tests;

public sealed class RosterLowPoseTests
{
    [Theory]
    [InlineData(CharacterClass.FightGuy)]
    [InlineData(CharacterClass.Bonk)]
    [InlineData(CharacterClass.Wibou)]
    public void AdmittedLowPostures_DuckHighContactButRemainHittableLow(CharacterClass character)
    {
        var entry = BuiltInContentResolver.Resolve(character);
        var def = entry.Definition;
        ServerSimulation Make(ActionState action)
        {
            var simulation = TestHelpers.MakeSim(TestHelpers.TestArena());
            var state = TestHelpers.PlayerState(50f, 50f) with
            {
                PY = TestHelpers.GroundPY(def),
                IsGrounded = true,
                State = action,
                VX = action == ActionState.Sliding ? def.Movement.RunSpeed * .5f : 0f,
            };
            simulation.RegisterEntity(1, def, state, entry.BakedAnimation);
            simulation.Tick(new Dictionary<ulong, InputState>
            {
                [1] = new() { Down = action != ActionState.Idle },
            });
            return simulation;
        }

        var standing = Make(ActionState.Idle);
        var crouching = Make(ActionState.Crouching);
        var sliding = Make(ActionState.Sliding);
        var upper = standing.GetLastEntityData()
            .OrderByDescending(e => (e.Shape == HitboxShape.Capsule && e.EndY > e.PosY ? e.EndY : e.PosY) + e.Radius).First();
        var lower = crouching.GetLastEntityData().OrderBy(e => e.PosY).First();
        bool useEnd = upper.Shape == HitboxShape.Capsule && upper.EndY > upper.PosY;
        float highX = useEnd ? upper.EndX : upper.PosX;
        float highY = (useEnd ? upper.EndY : upper.PosY) + upper.Radius - .04f;
        float highZ = useEnd ? upper.EndZ : upper.PosZ;

        Assert.True(Contact(standing, highX, highY, highZ, .05f, false));
        Assert.False(Contact(crouching, highX, highY, highZ, .05f, true));
        Assert.False(Contact(sliding, highX, highY, highZ, .05f, true));
        Assert.True(Contact(Make(ActionState.Crouching), lower.PosX, lower.PosY, lower.PosZ, .2f, true));
        Assert.True(Contact(Make(ActionState.Sliding), lower.PosX, lower.PosY, lower.PosZ, .2f, true));
    }

    private static bool Contact(ServerSimulation simulation, float x, float y, float z, float radius, bool down)
    {
        simulation.Resolver.Spawn(new Hitbox
        {
            OwnerId = 2, X = x, Y = y, Z = z, Radius = radius,
            Shape = HitboxShape.Sphere, Damage = 1, DurationTicks = 2,
        });
        simulation.Tick(new Dictionary<ulong, InputState> { [1] = new() { Down = down } });
        return simulation.LastTickHits.Any(hit => hit.TargetEntityId == 1);
    }
}

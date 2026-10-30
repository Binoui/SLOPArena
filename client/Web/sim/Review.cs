using System.Runtime.InteropServices.JavaScript;
using SlopArena.Shared;

public static partial class Demo
{
    static int _reviewSlot, _reviewHold;
    static bool _reviewShield;
    static float _reviewYaw, _reviewDistance;

    [JSExport]
    public static string ReviewStart(string attacker, string victim, int slot, double distance, int damage, int holdTicks, int shield)
    {
        var mode = new ReviewMode();
        mode.Arena = _arena;
        mode.Sim = new ServerSimulation(_arena, NoWinMatchRule.Instance);
        mode.Distance = (float)distance;
        mode.Damage = (ushort)damage;
        var error = mode.Begin(new[] { attacker, victim }, _catalog!, 0, 1);
        if (error != null) return "{\"error\":" + Str(error) + "}";
        _mode = mode;
        _tick = 0;
        _reviewSlot = slot; _reviewHold = holdTicks; _reviewShield = shield != 0;
        _reviewYaw = _arena.SpawnPoints[0].Yaw;
        _reviewDistance = (float)distance;
        return Frame();
    }

    [JSExport]
    public static string ReviewStep()
    {
        static short Yaw(float v) => (short)Math.Clamp(MathF.Round(MathF.IEEERemainder(v * 180f / MathF.PI, 360f) * 100f), -18000f, 18000f);
        _inputs.Clear();
        _inputs[1] = new InputState {
            FacingYaw = Yaw(_reviewYaw), AimYaw = Yaw(_reviewYaw), TargetEntityId = 2,
            AimDistance = (ushort)MathF.Round(_reviewDistance * 100f),
            ActiveSlot = _tick == 40 ? (byte)_reviewSlot : AbilitySlots.None,
            IsAiming = _reviewHold > 0 && _tick >= 40 && _tick < 40 + _reviewHold
        };
        _inputs[2] = new InputState { FacingYaw = Yaw(_reviewYaw + MathF.PI), AimYaw = Yaw(_reviewYaw + MathF.PI), ShieldHeld = _reviewShield };
        _mode!.Sim.Tick(_inputs);
        _tick++;
        return Frame();
    }
}

public sealed class ReviewMode : MatchMode
{
    public float Distance;
    public ushort Damage;
    public override IMatchRule Rule => NoWinMatchRule.Instance;
    public override bool Ended => false;
    public override InputState HumanInput(Fighter f, InputState input) => input;
    public override void WriteFrame(System.Text.StringBuilder sb) => sb.Append("null");
    public override string? Begin(string[] packages, MatchContentCatalog catalog, int difficulty, int seed)
    {
        var spawn = Arena.SpawnPoints[0];
        for (int i = 0; i < 2; i++) {
            var entry = catalog.ResolvePackage(packages[i]);
            if (entry == null) return "missing package " + packages[i];
            float offset = (i == 0 ? -0.5f : 0.5f) * Distance;
            var point = new SpawnPoint { X = spawn.X + MathF.Sin(spawn.Yaw) * offset, Y = spawn.Y,
                Z = spawn.Z + MathF.Cos(spawn.Yaw) * offset, Yaw = spawn.Yaw + i * MathF.PI };
            var fighter = Register((ulong)i + 1, entry, point, false, seed);
            if (i == 1) { var state = Sim.GetState(fighter.Id); state.DamagePercent = Damage; Sim.SetState(fighter.Id, state); }
        }
        return null;
    }
}

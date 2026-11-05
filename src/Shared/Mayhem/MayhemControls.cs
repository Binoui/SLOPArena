using System;

namespace SlopArena.Shared.Mayhem;

/// <summary>
/// The control gate. Frozen admits nothing. Playing always admits move, walk, jumps, normals 1 to 3, aim and
/// facing, plus at most one <see cref="ExtraVerb"/>. Down actions, lock toggle and retarget are always off.
/// Shared applies it to raw input, buffered slots, actual activation and movement admission.
/// </summary>
public readonly struct MayhemControls : IEquatable<MayhemControls>
{
    private MayhemControls(bool playing, ExtraVerb extra) { IsPlaying = playing; Extra = extra; }

    public bool IsPlaying { get; }
    public ExtraVerb Extra { get; }

    public static MayhemControls Frozen => default;
    public static MayhemControls Playing(ExtraVerb extra) => new(true, extra);

    /// <summary>Whether a wire slot (<see cref="AbilitySlots"/>) may start a move.</summary>
    public bool AdmitsSlot(byte slot) => IsPlaying && slot switch
    {
        AbilitySlots.Normal1 or AbilitySlots.Normal2 or AbilitySlots.Normal3 => true,
        AbilitySlots.A or AbilitySlots.E or AbilitySlots.R or AbilitySlots.F => Extra == ExtraVerb.Specials,
        _ => false,
    };

    public InputState Filter(in InputState raw)
    {
        if (!IsPlaying) return default;
        var input = new InputState
        {
            Up = raw.Up, Left = raw.Left, Right = raw.Right,
            Jump = raw.Jump, JumpHeld = raw.JumpHeld, Walk = raw.Walk,
            MoveX = raw.MoveX, MoveY = raw.MoveY, FaceToCamera = raw.FaceToCamera,
            FacingYaw = raw.FacingYaw, AimYaw = raw.AimYaw, AimPitch = raw.AimPitch, AimDistance = raw.AimDistance,
            ActiveSlot = AdmitsSlot(raw.ActiveSlot) ? raw.ActiveSlot : (byte)0,
        };
        switch (Extra)
        {
            case ExtraVerb.None:
                break;
            case ExtraVerb.Shield:
                input.ShieldHeld = raw.ShieldHeld; input.ShieldPressed = raw.ShieldPressed;
                break;
            case ExtraVerb.Grab:
                input.GrabPressed = raw.GrabPressed;
                break;
            case ExtraVerb.Specials:
                input.IsAiming = raw.IsAiming;
                break;
            case ExtraVerb.Beeline:
                input.DashPressed = raw.DashPressed; input.TargetEntityId = raw.TargetEntityId;
                break;
        }
        return input;
    }

    /// <summary>Drops a buffered press the gate no longer admits.</summary>
    public void ClearForbidden(ref CharacterState state)
    {
        if (state.BufferedSlot != 0 && !AdmitsSlot(state.BufferedSlot)) state.BufferedSlot = 0;
    }

    public bool Equals(MayhemControls other) => IsPlaying == other.IsPlaying && Extra == other.Extra;
    public override bool Equals(object? obj) => obj is MayhemControls other && Equals(other);
    public override int GetHashCode() => (IsPlaying ? 1 : 0) | (int)Extra << 1;
    public static bool operator ==(MayhemControls a, MayhemControls b) => a.Equals(b);
    public static bool operator !=(MayhemControls a, MayhemControls b) => !a.Equals(b);
    public override string ToString() => IsPlaying ? $"Playing({Extra})" : "Frozen";
}

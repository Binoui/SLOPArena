namespace SlopArena.Shared.Abilities;

public enum HoldCatch : byte { Capture, LandedHit }

public interface ICommandHold
{
    HoldCatch CatchKind { get; }
    bool Catches { get; }
    bool Holding { get; }
    bool ReleaseRequested { get; }
    float? AnchorDistance { get; }
    float AnchorHeight { get; }
    CookedGrabLaunch? Launch { get; }
    ushort ReleaseLockTicks { get; }
    void OnCaught(ref CharacterState owner);
    void OnClashed(ref CharacterState owner);
    void OnPlaced(ref CharacterState owner, bool blocked);
    void OnReleased(ref CharacterState owner);
}

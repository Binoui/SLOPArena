namespace SlopArena.Shared.Abilities;

/// <summary>Shared contact and pair lifecycle for a tether catch.</summary>
public interface ITetherCatch
{
    bool Catches { get; }
    bool Reeling { get; }
    bool Launches { get; }
    void OnCaught(ref CharacterState owner, float victimX, float victimY, float victimZ);
}

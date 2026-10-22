namespace SlopArena.Shared.Abilities;

/// <summary>
/// Marker for capabilities that own the end of their move: they must receive a tick after landing to
/// complete their action, and their stage's clock does not end the move while they run (a leap's landing,
/// a charge's gallop and ending).
/// </summary>
public interface ILandingContinuationCapability
{
}

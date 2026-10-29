namespace SlopArena.Shared;

internal static class HeldPress
{
    private const byte GrabMarker = 255;

    internal static void CaptureLatest(ref CharacterState state, in InputState input)
    {
        if (input.GrabPressed) state.BufferedSlot = GrabMarker;
        if (input.ActiveSlot != 0) state.BufferedSlot = input.ActiveSlot;
    }

    internal static bool IsGrab(in CharacterState state) => state.BufferedSlot == GrabMarker;
}

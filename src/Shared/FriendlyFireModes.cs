namespace SlopArena.Shared;

public static class FriendlyFireModes
{
    public static string ToWire(this FriendlyFireMode mode) => mode switch
    {
        FriendlyFireMode.Off => "off",
        FriendlyFireMode.Shoves => "shoves",
        FriendlyFireMode.On => "on",
        _ => throw new System.ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParseWire(string? value, out FriendlyFireMode mode)
    {
        mode = value switch
        {
            "off" => FriendlyFireMode.Off,
            "shoves" => FriendlyFireMode.Shoves,
            "on" => FriendlyFireMode.On,
            _ => (FriendlyFireMode)255,
        };
        return mode <= FriendlyFireMode.On;
    }
}

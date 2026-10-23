using System;
using System.IO;

namespace SlopArena.Shared;

/// <summary>The name and SHA-256 digest of an admitted baked arena.</summary>
public sealed record MatchArenaRecord
{
    public string Name { get; }
    public string Digest { get; }

    public MatchArenaRecord(string name, string digest)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Arena name is required.", nameof(name));
        if (digest == null || digest.Length != 64 ||
            System.Linq.Enumerable.Any(digest, c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Arena digest must be lowercase SHA-256.", nameof(digest));
        Name = name;
        Digest = digest;
    }

    /// <summary>Hashes and parses one buffer; never reconstructs the baked bytes from a definition.</summary>
    public static bool TryLoad(string path, string expectedName, out MatchArenaRecord? identity,
        out ArenaDefinition arena, out string? error)
    {
        identity = null;
        arena = default;
        error = null;
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (ArenaBinaryFormat.Deserialize(bytes) is not ArenaDefinition parsed)
            { error = $"Baked arena '{expectedName}' could not be parsed."; return false; }
            if (!string.Equals(parsed.Name, expectedName, StringComparison.Ordinal))
            { error = $"Baked arena name '{parsed.Name}' does not match '{expectedName}'."; return false; }
            identity = new MatchArenaRecord(parsed.Name, MatchContentInternals.Sha256(bytes));
            arena = parsed;
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException)
        {
            error = $"Baked arena '{expectedName}' could not be loaded: {ex.Message}";
            return false;
        }
    }

    public bool TryLoadVerified(string path, out ArenaDefinition arena, out string? error)
    {
        if (!TryLoad(path, Name, out var actual, out arena, out error)) return false;
        if (actual == this) return true;
        arena = default;
        error = $"Baked arena '{Name}' digest does not match the admitted match content.";
        return false;
    }
}

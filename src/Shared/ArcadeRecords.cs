using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SlopArena.Shared;

public sealed record ArcadeRecordKey(string ChallengeId, string ProfileId, string FighterPackageId, string GameplayHash)
{
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ChallengeId) || ChallengeId.Length > 128
            || string.IsNullOrWhiteSpace(ProfileId) || ProfileId.Length > 256
            || string.IsNullOrWhiteSpace(FighterPackageId) || FighterPackageId.Length > 256
            || GameplayHash == null || GameplayHash.Length != 64
            || GameplayHash.Any(x => !(x >= '0' && x <= '9' || x >= 'a' && x <= 'f')))
            throw new InvalidDataException("Invalid Arcade record identity.");
    }
    internal bool SameChallenge(ArcadeRecordKey other) => ChallengeId == other.ChallengeId
        && ProfileId == other.ProfileId && FighterPackageId == other.FighterPackageId;
}

public sealed record ArcadeRecordProvenance(string FighterVersion, string CookedContentHash,
    string PackageHash, string StageHash);
public sealed record ArcadePersonalBest(ArcadeRecordKey Key, ulong Value, string AchievedUtc,
    ArcadeRecordProvenance Provenance, bool Archived);
public sealed record ArcadeRecordUpdate(ulong? PreviousBest, ulong PersonalBest, bool Improved);

/// <summary>Local personal records only. No account, preferences reset, or online service owns them.</summary>
public sealed class ArcadeRecordBook
{
    private readonly List<ArcadePersonalBest> _records;
    public IReadOnlyList<ArcadePersonalBest> Records => new ReadOnlyCollection<ArcadePersonalBest>(_records);
    public ArcadeRecordBook() => _records = new();
    private ArcadeRecordBook(List<ArcadePersonalBest> records) => _records = records;

    public ulong? Best(ArcadeRecordKey key)
    {
        key.Validate();
        return _records.FirstOrDefault(x => x.Key == key)?.Value;
    }

    public ArcadeRecordUpdate BankSlopball(ArcadeRecordKey key, ulong returns,
        ArcadeRecordProvenance provenance, DateTime achievedUtc)
    {
        key.Validate();
        if (key.ChallengeId != "slopball") throw new ArgumentException("This result must be a Slopball return count.");
        return Bank(key, returns, provenance, achievedUtc, lowerIsBetter: false);
    }

    /// <summary>Only an immutable completed Shared attempt can supply an eligible course time.</summary>
    public ArcadeRecordUpdate BankCourse(ArcadeRecordKey key, CharacterCourseState result,
        ArcadeRecordProvenance provenance, DateTime achievedUtc)
    {
        key.Validate();
        if (key.ChallengeId != "character-course" || result.Phase != CharacterCoursePhase.Completed
            || result.ElapsedTicks == 0 || result.TotalTargets == 0 || result.BrokenTargets != result.TotalTargets)
            throw new ArgumentException("Course records require all targets and a completed timed run.");
        return Bank(key, result.ElapsedTicks, provenance, achievedUtc, lowerIsBetter: true);
    }

    private ArcadeRecordUpdate Bank(ArcadeRecordKey key, ulong value,
        ArcadeRecordProvenance provenance, DateTime achievedUtc, bool lowerIsBetter)
    {
        if (provenance == null) throw new ArgumentNullException(nameof(provenance));
        ulong? previous = Best(key);
        for (int i = 0; i < _records.Count; i++)
            if (_records[i].Key.SameChallenge(key))
                _records[i] = _records[i] with { Archived = _records[i].Key != key };
        bool improved = !previous.HasValue || (lowerIsBetter ? value < previous.Value : value > previous.Value);
        if (improved)
        {
            _records.RemoveAll(x => x.Key == key);
            _records.Add(new(key, value, achievedUtc.ToUniversalTime().ToString("O"), provenance, false));
        }
        return new(previous, improved ? value : previous!.Value, improved);
    }

    public string Serialize()
    {
        string json = JsonSerializer.Serialize(new Document(1, _records));
        if (_records.Count > 10000 || json.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Arcade records exceed the file limit.");
        return json;
    }

    public static ArcadeRecordBook Deserialize(string json)
    {
        if (json == null || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Arcade records exceed the file limit.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(json);
            if (document == null || document.Version != 1 || document.Records == null || document.Records.Count > 10000)
                throw new InvalidDataException("Unsupported or invalid Arcade records file.");
            var keys = new HashSet<ArcadeRecordKey>();
            foreach (var record in document.Records)
            {
                if (record?.Key == null || record.Provenance == null
                    || !DateTime.TryParseExact(record.AchievedUtc, "O", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    throw new InvalidDataException("Invalid Arcade personal best.");
                record.Key.Validate();
                if (!keys.Add(record.Key)) throw new InvalidDataException("Duplicate Arcade personal best.");
            }
            return new(document.Records);
        }
        catch (JsonException e) { throw new InvalidDataException("Arcade records could not be read.", e); }
    }

    private sealed record Document(int Version, List<ArcadePersonalBest> Records);
}

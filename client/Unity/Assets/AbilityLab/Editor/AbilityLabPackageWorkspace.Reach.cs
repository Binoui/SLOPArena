using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SlopArena.Client;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

internal sealed class AbilityLabReachObservation
{
    internal AbilityLabReachObservation(HitReliabilityDocument document, CharacterDefinition definition,
        BakedAnimationData poses, string sourceHash, string targetStamp, double elapsedMs)
    {
        Document = document;
        Definition = definition;
        Poses = poses;
        SourceHash = sourceHash;
        TargetStamp = targetStamp;
        ElapsedMs = elapsedMs;
    }

    internal HitReliabilityDocument Document { get; }
    internal MoveReliability Move => Document.Moves[0];
    internal CharacterDefinition Definition { get; }
    internal BakedAnimationData Poses { get; }
    internal string SourceHash { get; }
    internal string TargetStamp { get; }
    internal double ElapsedMs { get; }
}

internal sealed class AbilityLabReachException : Exception
{
    internal AbilityLabReachException(string code, string message) : base(message) => Code = code;
    internal string Code { get; }
}

public sealed partial class AbilityLabPackageWorkspace
{
    internal AbilityLabReachObservation? Reach { get; private set; }
    private static HitReliabilityTargets? s_reachTargets;
    private static string s_reachTargetStamp = "";
    private string _reachObservedTargetStamp = "";
    private CharacterAuthoringDocument? _reachHashDraft;
    private PackageManifestSource? _reachHashManifest;
    private string _reachSourceHash = "";

    private string ReachSourceHash()
    {
        if (!ReferenceEquals(_reachHashDraft, Draft) || !ReferenceEquals(_reachHashManifest, Manifest))
        {
            _reachSourceHash = ReachHash(CharacterPackageSourceCodec.SerializeManifest(Manifest) + "\n" +
                CharacterPackageSourceCodec.SerializeCharacter(Draft));
            _reachHashDraft = Draft;
            _reachHashManifest = Manifest;
        }
        return _reachSourceHash;
    }

    internal AbilityLabReachObservation MeasureReach(SlotAddress slot)
    {
        if (!HasPackage || !PrepareScenarioPreview() || PreviewLab == null)
            throw new AbilityLabReachException("preview.unavailable", "The current draft needs valid poses, catalog and rig.");
        PreviewLab.ApplyPackageDraftPreview(LiveDraftPackage!, Preview);
        var definition = PreviewLab.Def;
        var poses = PreviewLab.Baked;
        if (!PreviewLab.IsPackagePreview || definition == null || poses == null)
            throw new AbilityLabReachException("preview.unavailable", "The live draft preview is unavailable.");
        string sourceHash = ReachSourceHash();
        var clock = Stopwatch.StartNew();
        HitReliabilityTargets targets;
        string stamp;
        try
        {
            stamp = ReachTargetStamp(out string root);
            if (s_reachTargets == null || s_reachTargetStamp != stamp)
            {
                var loaded = HitReliabilityTargets.Load(root);
                s_reachTargets = loaded;
                s_reachTargetStamp = stamp;
            }
            targets = s_reachTargets;
        }
        catch (Exception ex)
        {
            throw new AbilityLabReachException("reach.targets.unavailable", ex.Message);
        }
        var move = HitReliability.Measure(definition, poses, slot, targets)
            ?? throw new AbilityLabReachException("reach.action.unavailable", $"Slot '{slot.Id}' has no move in this draft.");
        if (stamp != ReachTargetStamp(out _) || !ReferenceEquals(definition, PreviewLab.Def) ||
            !ReferenceEquals(poses, PreviewLab.Baked) || sourceHash != ReachSourceHash())
            throw new AbilityLabReachException("reach.inputs.changed", "Reach inputs changed during measurement. Run Reach again.");
        var document = new HitReliabilityDocument(Preview.Identity, new[] { move }, targets, DateTime.UtcNow,
            HitReliabilityDraft.Of(LiveDraftPackage!, HasUnsavedChanges, SelectedForm, targets));
        clock.Stop();
        _reachObservedTargetStamp = stamp;
        return Reach = new AbilityLabReachObservation(document, definition, poses, sourceHash, stamp, clock.Elapsed.TotalMilliseconds);
    }

    internal bool ReachMatchesSelection(string slot) => Reach != null && HasPackage &&
        Reach.Document.Identity.PackageId == PackageId && Reach.Definition.Form == SelectedForm && Reach.Move.Slot == slot;

    internal bool IsReachCurrent(string slot) => ReachMatchesSelection(slot) && !LiveDraftInvalid &&
        !PreviewPrerequisitesStale && Preview?.IsAvailable == true && PreviewLab?.IsPackagePreview == true &&
        ReferenceEquals(Reach!.Definition, PreviewLab.Def) && ReferenceEquals(Reach.Poses, PreviewLab.Baked) &&
        Reach.SourceHash == ReachSourceHash() && Reach.TargetStamp == _reachObservedTargetStamp;

    internal void RefreshReachTargetStamp()
    {
        if (Reach == null) return;
        try { _reachObservedTargetStamp = ReachTargetStamp(out _); }
        catch { _reachObservedTargetStamp = ""; }
    }

    private static string ReachTargetStamp(out string root)
    {
        var resolution = LocalContentResolver.CreateDefault().ResolveRoster();
        if (!resolution.Success) throw new InvalidDataException(string.Join("; ", resolution.Diagnostics.Select(d => d.Message)));
        root = resolution.RootPath;
        var paths = new[] { resolution.ManifestPath }.Concat(new[] { CharacterClass.FightGuy, CharacterClass.Manki }
            .Select(selector => Path.Combine(resolution.RootPath,
                resolution.Roster.Resolve(selector)?.PackageId ?? throw new InvalidDataException($"Missing roster target {selector}."),
                "manifest.json")));
        return ReachHash(root + string.Concat(paths.Select(path => "\n" + path + "\n" + File.ReadAllText(path))));
    }

    private static string ReachHash(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
    }
}

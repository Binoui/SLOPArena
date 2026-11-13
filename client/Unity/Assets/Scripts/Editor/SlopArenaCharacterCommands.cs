using Unity.Pipeline.Commands;

public static class SlopArenaCharacterCommands
{
    [CliCommand(
        "sloparena.character.inspect",
        "Inspect a Character Package source, bindings, cooked status, hashes, and canonical slots.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageInspectionResult Inspect(
        [CliArg("target", "Package ID or package root inside Assets/CharacterPackages.", Required = true)] string target)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(target);

    [CliCommand(
        "sloparena.character.cook",
        "Validate and cook a Character Package through the canonical Unity asset pipeline.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageCookResult Cook(
        [CliArg("target", "Package ID or package root inside Assets/CharacterPackages.", Required = true)] string target,
        [CliArg("dry-run", "Validate and plan without writing cooked outputs.", Required = false, DefaultValue = false)] bool dryRun = false)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(target, dryRun);

    [CliCommand(
        "sloparena.character.create",
        "Create an empty Character Package and asset catalog.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageCreateResult Create(
        [CliArg("package-id", "Stable package ID.", Required = true)] string packageId,
        [CliArg("display-name", "Character display name.", Required = true)] string displayName,
        [CliArg("creator", "Creator name.", Required = false)] string creator,
        [CliArg("license", "License identifier.", Required = false)] string license,
        [CliArg("attribution", "Attribution text.", Required = false)] string attribution)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot())
            .NewPackage(packageId, displayName, creator ?? "Binoui", license ?? "MIT", attribution ?? "SlopArena");

    [CliCommand(
        "sloparena.character.bind",
        "Persist a typed AnimationClip binding in a Character Package catalog.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageBindingResult Bind(
        [CliArg("target", "Package ID or package root.", Required = true)] string target,
        [CliArg("semantic-id", "Catalog semantic animation ID.", Required = true)] string semanticId,
        [CliArg("asset-path", "Project-relative AnimationClip asset path.", Required = true)] string assetPath)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Bind(target, semanticId, assetPath);

    [CliCommand(
        "sloparena.character.unbind",
        "Clear a typed AnimationClip binding in a Character Package catalog.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageBindingResult Unbind(
        [CliArg("target", "Package ID or package root.", Required = true)] string target,
        [CliArg("semantic-id", "Catalog semantic animation ID.", Required = true)] string semanticId)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Unbind(target, semanticId);

    [CliCommand(
        "sloparena.character.verify",
        "Verify source, catalog, cooked artifact, hashes, and dependencies without mutation.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageVerificationResult Verify(
        [CliArg("target", "Package ID or package root.", Required = true)] string target)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Verify(target);

    [CliCommand(
        "sloparena.character.assets",
        "Discover compatible typed animation assets for a package semantic ID.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterPackageAssetDiscoveryResult Assets(
        [CliArg("target", "Package ID or package root.", Required = true)] string target,
        [CliArg("semantic-id", "Catalog semantic animation ID.", Required = true)] string semanticId)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).DiscoverAssets(target, semanticId);

    [CliCommand(
        "sloparena.character.roster.admit",
        "Explicitly admit a verified cooked package to the built-in roster.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterRosterAdmissionResult AdmitRoster(
        [CliArg("package-id", "Stable package ID.", Required = true)] string packageId,
        [CliArg("selector", "string roster selector.", Required = true)] string selector,
        [CliArg("version", "Verified package version.", Required = false)] string version,
        [CliArg("cooked-hash", "Verified cooked content hash.", Required = false)] string cookedHash,
        [CliArg("package-hash", "Verified package hash.", Required = false)] string packageHash)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot())
            .AdmitRoster(packageId, selector, version, cookedHash, packageHash);

    [CliCommand(
        "sloparena.character.roster.refresh",
        "Refresh the pinned requirement for an already-admitted cooked package.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/character" })]
    public static CharacterRosterAdmissionResult RefreshRoster(
        [CliArg("package-id", "Admitted package ID.", Required = true)] string packageId)
        => new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot())
            .RefreshRoster(packageId);

    [CliCommand(
        "sloparena.assets.inspect",
        "Inspect shortlisted environment prefabs and optionally render local thumbnails.",
        MainThreadRequired = true,
        Tags = new[] { "authoring/assets" })]
    public static object Inspect(
        [CliArg("workset", "Repository-relative workset JSON path.", Required = true)] string workset,
        [CliArg("output", "Repository-relative inspection JSON path under .asset-catalog-cache.", Required = true)] string output,
        [CliArg("render-thumbnails", "Render normalized prefab thumbnails and a contact sheet.", Required = false, DefaultValue = false)] bool renderThumbnails = false,
        [CliArg("compact", "Return only status counts, diagnostic codes, and evidence paths.", Required = false, DefaultValue = false)] bool compact = false)
    {
        AssetCatalogInspectionResult result = new AssetCatalogInspectionService(UnityCharacterAssetCooker.ProjectRoot())
            .Inspect(workset, output, renderThumbnails);
        return compact ? (object)result.ToCompact() : result;
    }
}

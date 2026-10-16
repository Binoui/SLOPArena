using System;
using System.IO;
using System.Linq;
using UnityEngine;
using SlopArena.Shared;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlopArena.Client;

public static class LocalContentResolverSelfTest
{
#if UNITY_EDITOR
    [MenuItem("Tools/SLOPArena/Tests/Local Content Resolver")]
#endif
    public static void Run()
    {
        string priorDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var resolver = LocalContentResolver.CreateDefault();
            var playerResolver = LocalContentResolver.CreateForMode(LocalContentMode.Player);
            if (!Path.IsPathRooted(resolver.ProjectRoot) || resolver.ContentRoots.Any(root => !Path.IsPathRooted(root)) ||
                playerResolver.ContentRoots.Count != 1 || !Path.IsPathRooted(playerResolver.ContentRoots[0]))
                throw new InvalidOperationException("Local resolver returned an invalid development/player root policy.");
            var roster = resolver.ResolveRoster();
            if (!roster.Success || roster.Roster == null)
                throw new InvalidOperationException("Valid rooted cooked roster could not be resolved: " + Format(roster));


            var unavailable = resolver.ResolveCookedPackage("unavailable-package");
            if (unavailable.Success || unavailable.Roster != null || !unavailable.Diagnostics.Any(d => d.Code == "content.package.missing"))
                throw new InvalidOperationException("Unavailable package resolved unexpectedly or did not fail closed: " + Format(unavailable));

            Debug.Log("[LocalContentResolverSelfTest] Passed rooted cooked roster, unavailable package, and cwd-independence checks.");

        }
        finally
        {
            Directory.SetCurrentDirectory(priorDirectory);
        }
    }

    private static string Format(LocalContentResolution resolution)
        => string.Join("; ", resolution.Diagnostics.Select(d => $"{d.Code} ({d.Path}): {d.Message}"));
}

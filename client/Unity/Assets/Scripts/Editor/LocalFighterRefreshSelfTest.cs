#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using SlopArena.Client;
using SlopArena.Client.UI;
using SlopArena.Shared;
using UnityEditor;
using UnityEngine;

public static class LocalFighterRefreshSelfTest
{
    [MenuItem("SLOPArena/Tests/Local fighter source refresh")]
    public static void Run()
    {
        string[] packages = { "wibou", "fightguy" };
        var paths = packages.Select(package => Path.Combine(Application.dataPath, "CharacterPackages", package, "character.json")).ToArray();
        var original = paths.Select(File.ReadAllBytes).ToArray();
        var savedPlayer = MatchConfig.Selection;
        var savedSeats = MatchConfig.SoloSeats.ToArray();
        var savedMode = MatchConfig.Mode;
        try
        {
            Require(ClientSession.TryBuildAvailableFighterCatalog(out var before, out var failure), failure);
            var player = before.ResolvePackage("wibou").Choose();
            var cpu = before.ResolvePackage("fightguy").Choose();
            MatchConfig.Mode = GameMode.Training;
            MatchConfig.SetPlayerFighter(player);
            MatchConfig.SoloSeats[0] = new SoloSeat { Kind = SeatKind.Cpu };
            MatchConfig.SoloSeats[0].SetFighter(cpu);
            var changed = packages.Select((package, index) =>
            {
                string metadata = File.ReadAllText(Path.Combine(Application.dataPath, "CharacterPackages", package, "package.json"));
                var source = CharacterPackageSourceCodec.Load(metadata, Encoding.UTF8.GetString(original[index])).Source;
                return CharacterPackageSourceCodec.SerializeCharacter(source.Character with
                { Movement = source.Character.Movement with { RunSpeed = source.Character.Movement.RunSpeed + 0.75f } });
            }).ToArray();
            File.WriteAllText(paths[1], changed[1], Encoding.UTF8);
            File.WriteAllText(paths[0], "{}", Encoding.UTF8);
            Require(!LocalFighterLibrary.Refresh().TryGetCatalog(out _, out _), "Invalid development content must not fall back to cooked content.");
            File.WriteAllText(paths[0], changed[0], Encoding.UTF8);
            Require(ClientSession.TryBuildLocalMatchCatalog(out var after, out failure), failure);
            Require(MatchConfig.Selection.Choice.Revision == after.ResolvePackage("wibou").Identity && MatchConfig.Selection.Choice.Revision != player.Revision,
                "P1 did not resolve the edited package without a domain reset.");
            Require(MatchConfig.Seat(2).Choice.Revision == after.ResolvePackage("fightguy").Identity && MatchConfig.Seat(2).Choice.Revision != cpu.Revision,
                "The CPU did not resolve the edited package without a domain reset.");
            Require(player.Resolve(before) != null && cpu.Resolve(before) != null && player.Resolve(after) == null && cpu.Resolve(after) == null,
                "Refreshing local picks changed a previously pinned catalog.");
            Require(ClientSession.TryBuildPersistedLocalMatchCatalog(out var cooked, out failure), failure);
            MatchConfig.SetPlayerFighter(cooked.ResolvePackage("wibou").Choose());
            Require(ClientSession.TryBuildLocalMatchCatalog(out var local, out failure)
                && MatchConfig.Selection.Choice.Resolve(local) != null && MatchConfig.Selection.Choice.Revision != cooked.ResolvePackage("wibou").Identity,
                "A cooked online selection did not resolve against current development content.");
            string oldKey = "SlopArena.ArcadeBallLab.Fighter";
            string newKey = SlopArena.Client.World.ArcadeBallMatch.FighterSessionKey;
            string oldSession = SessionState.GetString(oldKey, "");
            string newSession = SessionState.GetString(newKey, "");
            try
            {
                var migration = typeof(SlopArena.Client.World.ArcadeBallMatch).GetMethod("ReadEditorPackageOverride",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                SessionState.EraseString(newKey); SessionState.SetString(oldKey, "Wibou");
                Require((string)migration.Invoke(null, new object[] { "" }) == "wibou" && SessionState.GetString(newKey, "") == "wibou"
                    && SessionState.GetString(oldKey, "") == "", "Named Arcade session selection was not migrated.");
                SessionState.EraseString(newKey); SessionState.SetString(oldKey, "7");
                Require((string)migration.Invoke(null, new object[] { "" }) == ShippedRoster.ReadApplication().DefaultFighter.PackageId,
                    "A numeric legacy session value was reinterpreted as a package.");
            }
            finally { SessionState.SetString(oldKey, oldSession); SessionState.SetString(newKey, newSession); }
            Debug.Log("[LocalFighterRefreshSelfTest] Same-domain source edits, failed/fixed compile, P1/CPU refresh, pinned catalog and cooked-to-local transition passed. Play mode transitions were not entered.");
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++)
            {
                File.WriteAllBytes(paths[i], original[i]);
                AssetDatabase.ImportAsset("Assets/CharacterPackages/" + packages[i] + "/character.json", ImportAssetOptions.ForceUpdate);
            }
            LocalFighterLibrary.Invalidate();
            if (savedPlayer != null) MatchConfig.SetPlayerFighter(savedPlayer);
            Array.Copy(savedSeats, MatchConfig.SoloSeats, 3);
            MatchConfig.Mode = savedMode;
        }
    }

    static void Require(bool success, string message)
    { if (!success) throw new InvalidOperationException(message); }
}
#endif

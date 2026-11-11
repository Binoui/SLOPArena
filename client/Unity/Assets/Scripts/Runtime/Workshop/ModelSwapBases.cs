#nullable enable
using System.Collections.Generic;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// Which built-in fighters a model swap can dress (Workshop roadmap step 1.3). A fighter accepts one when its
    /// presentation never reshapes its own body, so a creator's humanoid can play its clips. A fighter missing from
    /// the table has not been checked and refuses.
    /// </summary>
    public static class ModelSwapBases
    {
        private static readonly Dictionary<string, string?> RefusalByPackage = new()
        {
            ["fightguy"] = null,
            ["bonk"] = null,
            ["wibou"] = null,
            ["manki"] = null,
            ["salamander"] = null,
            ["bashie"] = "Bashie's shell slide and whistle reshape her own model (ShellTuckRig, WhistlePuffRig); model swaps on her wait for a later step.",
            ["nyx"] = "Nyx shows one of two bodies by form (FormPresentation); model swaps on her wait for a later step.",
        };

        public static bool Accepts(string packageId, out string refusal)
        {
            if (RefusalByPackage.TryGetValue(packageId, out string? reason))
            {
                refusal = reason ?? "";
                return reason == null;
            }
            refusal = $"Model swaps have not been checked on {packageId} yet.";
            return false;
        }
    }
}

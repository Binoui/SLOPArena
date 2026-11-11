#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace SlopArena.Client.Workshop
{
    /// <summary>
    /// The model one player wears over their built-in fighter (a model swap, ADR-0065): a rigged humanoid GLB on
    /// disk. Its bone map file, when there is one, sits beside it.
    /// </summary>
    public enum ModelSwapChoiceSource { Player, LaunchOption, Folder }

    public sealed class ModelSwapChoice
    {
        public ModelSwapChoice(string glbPath, string? basePackageId = null, ModelSwapChoiceSource source = ModelSwapChoiceSource.Player) { GlbPath = glbPath; BasePackageId = basePackageId; Source = source; }
        public ModelSwapChoiceSource Source { get; }
        public string? BasePackageId { get; }

        public string GlbPath { get; }

        public string Name => Path.GetFileNameWithoutExtension(GlbPath);

        /// <summary><c>&lt;name&gt;.humanoid.json</c> for a loose GLB, else <c>humanoid.json</c> in a Workshop item folder.</summary>
        public string? BoneMapPath
        {
            get
            {
                string folder = Path.GetDirectoryName(GlbPath) ?? "";
                string named = Path.Combine(folder, Name + ".humanoid.json");
                if (File.Exists(named)) return named;
                string item = Path.Combine(folder, "humanoid.json");
                return File.Exists(item) ? item : null;
            }
        }
    }

}

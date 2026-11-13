#nullable enable
using SlopArena.Client.UI;

namespace SlopArena.Client.Workshop
{
    public static class FighterWorkspaceTraining
    {
        public static LocalFighterLibrary? Library { get; private set; }
        private static MadeFighterPresentation.Lease? presentation;
        private static string? folder;
        public static void Begin(LocalFighterLibrary library, MadeFighterPresentation.Lease lease, string source)
        {
            End(); Library = library; presentation = lease; folder = source;
        }
        public static string? End()
        {
            string? returning = folder;
            Library = null; folder = null; presentation?.Dispose(); presentation = null;
            return returning;
        }
    }
}

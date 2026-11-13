#nullable enable
using SlopArena.Client.UI;
using System;
using UnityEngine;

namespace SlopArena.Client.Workshop
{
    public static class FighterWorkspaceTraining
    {
        public static LocalFighterLibrary? Library { get; private set; }
        private static MadeFighterPresentation.Lease? presentation;
        private static string? folder;
        private static IDisposable? diskLease;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPlaySession() { Library = null; folder = null; presentation?.Dispose(); presentation = null; diskLease?.Dispose(); diskLease = null; }
        public static void Begin(LocalFighterLibrary library, MadeFighterPresentation.Lease lease, string source, IDisposable? disk = null)
        {
            End(); Library = library; presentation = lease; folder = source; diskLease = disk;
        }
        public static string? End()
        {
            string? returning = folder;
            Library = null; folder = null; if (presentation != null) FighterPresentationReleases.AfterRenderers(presentation); presentation = null;
            diskLease?.Dispose(); diskLease = null;
            return returning;
        }
    }
}

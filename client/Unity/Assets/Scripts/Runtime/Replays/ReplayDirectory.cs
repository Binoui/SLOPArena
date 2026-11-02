using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace SlopArena.Client.Replays
{
    public enum ReplayHostKind { Linux, Windows, WindowsUnderWine, MacOS }

    /// <summary>Everything the folder rule reads, so the rule is a pure function with test vectors.</summary>
    public sealed class ReplayDirectoryProbe
    {
        public ReplayDirectoryProbe(ReplayHostKind host, Func<string, string?> environment)
        {
            Host = host;
            Environment = environment;
        }

        public ReplayHostKind Host { get; }
        public Func<string, string?> Environment { get; }

        public static ReplayDirectoryProbe Current() => new(CurrentHost(), System.Environment.GetEnvironmentVariable);

        private static ReplayHostKind CurrentHost() => Application.platform switch
        {
            RuntimePlatform.WindowsEditor or RuntimePlatform.WindowsPlayer => UnderWine() ? ReplayHostKind.WindowsUnderWine : ReplayHostKind.Windows,
            RuntimePlatform.OSXEditor or RuntimePlatform.OSXPlayer => ReplayHostKind.MacOS,
            _ => ReplayHostKind.Linux,
        };

        // Wine and Proton export wine_get_version from ntdll; Windows does not.
        private static bool UnderWine()
        {
            try
            {
                var ntdll = GetModuleHandle("ntdll.dll");
                return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }

    /// <summary>
    /// Where Replays live, one rule for the Linux Editor, the Proton player and a Windows player. tools/replay_pull.py
    /// implements the same rule, and both are checked against tools/tests/replay_directory_vectors.json.
    /// 1. SLOPARENA_REPLAY_DIR wins; under Wine an absolute Unix value is reached through drive Z:.
    /// 2. Linux: $XDG_DATA_HOME/sloparena/replays, else $HOME/.local/share/sloparena/replays.
    ///    Wine/Proton: the host's same folder through Z: when Wine passes XDG_DATA_HOME or HOME through,
    ///    else %LOCALAPPDATA%\sloparena\replays. Windows: %LOCALAPPDATA%\sloparena\replays.
    ///    macOS: ~/Library/Application Support/sloparena/replays.
    /// </summary>
    public static class ReplayDirectory
    {
        public const string OverrideVariable = "SLOPARENA_REPLAY_DIR";

        public static string Resolve(ReplayDirectoryProbe probe)
        {
            string? Env(string name) => probe.Environment(name) is { Length: > 0 } value ? value : null;
            bool wine = probe.Host == ReplayHostKind.WindowsUnderWine;
            if (Env(OverrideVariable) is { } chosen)
                return wine && chosen.StartsWith("/", StringComparison.Ordinal) ? Dos(chosen) : chosen;
            switch (probe.Host)
            {
                case ReplayHostKind.Linux:
                    return Unix(Env("XDG_DATA_HOME") ?? Unix(Env("HOME") ?? "~", ".local/share"), "sloparena/replays");
                case ReplayHostKind.MacOS:
                    return Unix(Env("HOME") ?? "~", "Library/Application Support/sloparena/replays");
                case ReplayHostKind.WindowsUnderWine when Env("XDG_DATA_HOME") is { } data && data.StartsWith("/", StringComparison.Ordinal):
                    return Dos(Unix(data, "sloparena/replays"));
                case ReplayHostKind.WindowsUnderWine when Env("HOME") is { } home && home.StartsWith("/", StringComparison.Ordinal):
                    return Dos(Unix(home, ".local/share/sloparena/replays"));
                default:
                    return (Env("LOCALAPPDATA") ?? "C:") + "\\sloparena\\replays";
            }
        }

        public static bool IsReplayFile(string path)
            => path.EndsWith(".slopreplay", StringComparison.Ordinal) || path.EndsWith(".slopreplay.part", StringComparison.Ordinal);

        /// <summary>Cheap enough for Home: stops at the first Replay file.</summary>
        public static bool HasAnyReplay(string directory)
        {
            try
            {
                return Directory.Exists(directory)
                    && Directory.EnumerateFiles(directory, "*.slopreplay*", SearchOption.AllDirectories).Any(IsReplayFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string Unix(string parent, string child) => parent.TrimEnd('/') + "/" + child;
        private static string Dos(string unix) => "Z:" + unix.Replace('/', '\\');
    }
}

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SlopArena.Shared;

internal static class PortableItemFiles
{
    internal const int HumanoidBytes = 65536;
    internal const int TotalBytes = 64 * 1024 * 1024;
    internal static byte[] Read(string path, int limit, ref long total)
    {
        using var handle = OpenRegular(path, out long size);
        if (size < 0 || size > limit || size > TotalBytes - total)
            throw new InvalidDataException($"Byte limit exceeded for {Path.GetFileName(path)}; file {size}/{limit}, item {total + size}/{TotalBytes}.");
        using var stream = new FileStream(handle, FileAccess.Read);
        var bytes = new byte[checked((int)size)];
        int offset = 0, count;
        while (offset < bytes.Length && (count = stream.Read(bytes, offset, bytes.Length - offset)) != 0) offset += count;
        // Compare lengths without reading beyond the bounded allocation.
        if (offset != bytes.Length || stream.Length != size) throw new InvalidDataException("File changed during read.");
        total += size;
        return bytes;
    }

    private static SafeFileHandle OpenRegular(string path, out long size)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            int fd = Open(path, 0x800 | 0x20000 | 0x80000);
            if (fd < 0) throw new IOException("Cannot open regular file; errno " + Marshal.GetLastWin32Error());
            var handle = new SafeFileHandle((IntPtr)fd, true);
            if (Statx(fd, "", 0x1000, 0x201, out var stat) != 0 || (stat.Mode & 0xf000) != 0x8000)
            { handle.Dispose(); throw new InvalidDataException("Payload must be a regular file."); }
            size = checked((long)stat.Size);
            return handle;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot open regular file; error " + Marshal.GetLastWin32Error()); }
            if (GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0)
            { handle.Dispose(); throw new InvalidDataException("Payload must be a regular disk file without a reparse point."); }
            size = checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow));
            return handle;
        }
        throw new PlatformNotSupportedException("Portable item reads currently support Linux and Windows.");
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx { [FieldOffset(28)] public ushort Mode; [FieldOffset(40)] public ulong Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsInfo
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open(string path, int flags);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int Statx(int fd, string path, int flags, uint mask, out LinuxStatx stat);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out WindowsInfo info);
}

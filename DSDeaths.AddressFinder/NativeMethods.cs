using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DSDeaths.AddressFinder;

internal static class NativeMethods
{
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessQueryInformation = 0x0400;

    internal const uint MemCommit = 0x1000;

    internal const uint PageNoAccess = 0x01;
    internal const uint PageReadOnly = 0x02;
    internal const uint PageReadWrite = 0x04;
    internal const uint PageWriteCopy = 0x08;
    internal const uint PageExecuteRead = 0x20;
    internal const uint PageExecuteReadWrite = 0x40;
    internal const uint PageExecuteWriteCopy = 0x80;
    internal const uint PageGuard = 0x100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryBasicInformation
    {
        internal IntPtr BaseAddress;
        internal IntPtr AllocationBase;
        internal uint AllocationProtect;
        internal ushort PartitionId;
        internal ushort Reserved;
        internal nuint RegionSize;
        internal uint State;
        internal uint Protect;
        internal uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemInfo
    {
        internal ushort ProcessorArchitecture;
        internal ushort Reserved;
        internal uint PageSize;
        internal IntPtr MinimumApplicationAddress;
        internal IntPtr MaximumApplicationAddress;
        internal nuint ActiveProcessorMask;
        internal uint NumberOfProcessors;
        internal uint ProcessorType;
        internal uint AllocationGranularity;
        internal ushort ProcessorLevel;
        internal ushort ProcessorRevision;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern ProcessSafeHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWow64Process(
        ProcessSafeHandle process,
        [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        ProcessSafeHandle process,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nuint VirtualQueryEx(
        ProcessSafeHandle process,
        IntPtr address,
        out MemoryBasicInformation buffer,
        nuint bufferLength);

    [DllImport("kernel32.dll")]
    internal static extern void GetNativeSystemInfo(out SystemInfo systemInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}

internal sealed class ProcessSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal ProcessSafeHandle()
        : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}

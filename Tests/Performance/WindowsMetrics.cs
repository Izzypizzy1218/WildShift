using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WildShiftPerformanceValidation
{
    internal struct MachineSample
    {
        public long WorkingSet, PrivateBytes, ProcessCpu100ns, ManagedHeap, AllocatedBytes;
        public ulong AvailablePhysical, TotalPhysical, SystemIdle100ns, SystemTotal100ns;
        public uint PageFaults;
        public int Gen0, Gen1, Gen2;
    }

    // Windows native counters avoid a new Process object/Refresh allocation per
    // sample. All metric probes run outside the native per-tick timing region.
    internal static class WindowsMetrics
    {
        [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessMemory
        {
            public uint Size, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("psapi.dll", SetLastError = true)] private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemory memory, uint size);
        private static readonly Func<bool, long> totalAllocated = TotalAllocator();
        private static readonly Func<long> threadAllocated = ThreadAllocator();
        public static string AllocationScope { get { return totalAllocated != null ? "all managed threads" : threadAllocated != null ? "game main managed thread only" : "unsupported on this runtime"; } }

        private static Func<bool, long> TotalAllocator()
        {
            MethodInfo method = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
            return method == null ? null : (Func<bool, long>)Delegate.CreateDelegate(typeof(Func<bool, long>), method);
        }
        private static Func<long> ThreadAllocator()
        {
            MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            return method == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
        }
        public static MachineSample Capture()
        {
            var memory = new ProcessMemory { Size = (uint)Marshal.SizeOf(typeof(ProcessMemory)) };
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            FileTime creation, exit, kernel, user, idle, systemKernel, systemUser;
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref memory, memory.Size)
                || !GetProcessTimes(GetCurrentProcess(), out creation, out exit, out kernel, out user)
                || !GetSystemTimes(out idle, out systemKernel, out systemUser) || !GlobalMemoryStatusEx(ref status))
                throw new InvalidOperationException("Native Windows performance counter query failed: " + Marshal.GetLastWin32Error());
            return new MachineSample {
                WorkingSet = checked((long)memory.WorkingSetSize.ToUInt64()), PrivateBytes = checked((long)memory.PrivateUsage.ToUInt64()),
                ProcessCpu100ns = checked((long)(kernel.Value + user.Value)), PageFaults = memory.PageFaultCount,
                AvailablePhysical = status.AvailablePhysical, TotalPhysical = status.TotalPhysical,
                SystemIdle100ns = idle.Value, SystemTotal100ns = systemKernel.Value + systemUser.Value,
                ManagedHeap = GC.GetTotalMemory(false),
                AllocatedBytes = totalAllocated != null ? totalAllocated(false) : threadAllocated != null ? threadAllocated() : -1,
                Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2)
            };
        }
    }
}

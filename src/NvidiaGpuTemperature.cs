using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XiaoJingDesktopPet
{
    internal sealed class GpuTemperatureReading
    {
        internal double? Temperature;
        internal string Name, Error;
        internal string Source = "NVIDIA NVML（驱动直读）";
    }
    internal interface INvmlTemperatureApi
    {
        int Initialize();
        int DeviceCount(out uint count);
        int DeviceByIndex(uint index, out IntPtr device);
        int Temperature(IntPtr device, out uint temperature);
        int DeviceName(IntPtr device, out string name);
        int Shutdown();
    }
    internal sealed class NvidiaGpuTemperature
    {
        private readonly Func<INvmlTemperatureApi> apiFactory;
        private readonly object gate = new object();
        private INvmlTemperatureApi api;
        private volatile bool stopped;
        internal NvidiaGpuTemperature() : this(delegate { return NativeNvmlTemperatureApi.Shared; }) { }
        internal NvidiaGpuTemperature(Func<INvmlTemperatureApi> apiFactory) { this.apiFactory = apiFactory; }
        internal void Stop() { stopped = true; }
        internal GpuTemperatureReading Read(Func<bool> cancelled)
        {
            if (IsStopped(cancelled)) return Unavailable("查询已停止");
            lock (gate)
            {
                if (IsStopped(cancelled)) return Unavailable("查询已停止");
                try
                {
                    if (api == null) api = apiFactory();
                    if (IsStopped(cancelled)) return Unavailable("查询已停止");
                    int status = api.Initialize();
                    if (status != 0) return Unavailable("驱动初始化失败，错误码 " + status);
                    try
                    {
                        if (IsStopped(cancelled)) return Unavailable("查询已停止");
                        uint count; status = api.DeviceCount(out count);
                        if (status != 0 || count > 256) return Unavailable("显卡枚举失败，错误码 " + status);
                        var result = new GpuTemperatureReading();
                        for (uint i = 0; i < count; i++)
                        {
                            if (IsStopped(cancelled)) return Unavailable("查询已停止");
                            IntPtr device; uint temperature;
                            if (api.DeviceByIndex(i, out device) != 0 || device == IntPtr.Zero) continue;
                            if (IsStopped(cancelled)) return Unavailable("查询已停止");
                            if (api.Temperature(device, out temperature) != 0 || temperature > 150) continue;
                            if (IsStopped(cancelled)) return Unavailable("查询已停止");
                            if (!result.Temperature.HasValue || temperature > result.Temperature.Value)
                            {
                                string name; api.DeviceName(device, out name);
                                result.Temperature = temperature;
                                result.Name = String.IsNullOrWhiteSpace(name) ? "NVIDIA GPU" : name;
                            }
                        }
                        if (!result.Temperature.HasValue) result.Error = "没有可用的 NVIDIA 显卡温度读数";
                        return result;
                    }
                    finally { try { api.Shutdown(); } catch (Exception) { } }
                }
                catch (Exception) { return Unavailable("NVIDIA 驱动直读接口不可用"); }
            }
        }
        private bool IsStopped(Func<bool> cancelled) { return stopped || Environment.HasShutdownStarted || (cancelled != null && cancelled()); }
        private static GpuTemperatureReading Unavailable(string error) { return new GpuTemperatureReading { Error = error }; }
    }
    internal sealed class NativeNvmlTemperatureApi : INvmlTemperatureApi
    {
        private static readonly Lazy<NativeNvmlTemperatureApi> instance = new Lazy<NativeNvmlTemperatureApi>(delegate { return new NativeNvmlTemperatureApi(); });
        internal static NativeNvmlTemperatureApi Shared { get { return instance.Value; } }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFunction();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CountFunction(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeviceFunction(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TemperatureFunction(IntPtr device, uint sensor, out uint temperature);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)] private delegate int NameFunction(IntPtr device, StringBuilder name, uint length);
        private readonly InitFunction initialize, shutdown;
        private readonly CountFunction count;
        private readonly DeviceFunction device;
        private readonly TemperatureFunction temperature;
        private readonly NameFunction name;
        // Keep one driver-module reference for this process. Each read balances
        // nvmlInit_v2/nvmlShutdown; the DLL is never unloaded under an active call.
        private readonly IntPtr module;
        private NativeNvmlTemperatureApi()
        {
            var candidates = new List<string> { Path.Combine(Environment.SystemDirectory, "nvml.dll") };
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programs, "NVIDIA Corporation", "NVSMI", "nvml.dll"));
            candidates.Add(Path.Combine(programs, "NVIDIA", "NVSMI", "nvml.dll"));
            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00000800);
                if (module != IntPtr.Zero) break;
            }
            if (module == IntPtr.Zero) throw new DllNotFoundException("NVIDIA NVML driver library unavailable");
            try
            {
                initialize = Bind<InitFunction>("nvmlInit_v2", "nvmlInit");
                shutdown = Bind<InitFunction>("nvmlShutdown");
                count = Bind<CountFunction>("nvmlDeviceGetCount_v2", "nvmlDeviceGetCount");
                device = Bind<DeviceFunction>("nvmlDeviceGetHandleByIndex_v2", "nvmlDeviceGetHandleByIndex");
                temperature = Bind<TemperatureFunction>("nvmlDeviceGetTemperature");
                name = Bind<NameFunction>("nvmlDeviceGetName");
            }
            catch { FreeLibrary(module); throw; }
        }
        private T Bind<T>(params string[] names) where T : class
        {
            foreach (string export in names)
            {
                IntPtr entry = GetProcAddress(module, export);
                if (entry != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer(entry, typeof(T)) as T;
            }
            throw new EntryPointNotFoundException(names[0]);
        }
        public int Initialize() { return initialize(); }
        public int DeviceCount(out uint value) { return count(out value); }
        public int DeviceByIndex(uint index, out IntPtr value) { return device(index, out value); }
        public int Temperature(IntPtr value, out uint degrees) { return temperature(value, 0, out degrees); }
        public int DeviceName(IntPtr value, out string text)
        { var buffer = new StringBuilder(256); int status = name(value, buffer, (uint)buffer.Capacity); text = buffer.ToString(); return status; }
        public int Shutdown() { return shutdown(); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr handle, string export);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeLibrary(IntPtr handle);
    }
}

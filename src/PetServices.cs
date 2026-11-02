using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace XiaoJingDesktopPet
{
    public sealed class WeatherInfo
    {
        public string Condition { get; set; }
        public int Severity { get; set; }
        public string Summary { get; set; }
        public double? Temperature { get; set; }
        public double? WindSpeed { get; set; }
        public double? WindGust { get; set; }
        // Equivalent hourly rates, normalized using the API's current.interval.
        public double? Precipitation { get; set; }
        public double? Snowfall { get; set; }
        public int? WeatherCode { get; set; }
        public bool? IsDay { get; set; }
        public DateTime? ObservedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsStale { get; set; }
        public string Error { get; set; }
        public long LocationVersion { get; set; }
    }

    public sealed class HardwareInfo
    {
        public double? CpuTemperature { get; set; }
        public double? GpuTemperature { get; set; }
        public double? MemoryPercent { get; set; }
        public double? MemoryUsedGiB { get; set; }
        public double? MemoryTotalGiB { get; set; }
        public string GpuName { get; set; }
        public string GpuSensorSource { get; set; }
        public string CpuSensorSource { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Error { get; set; }
    }

    public sealed class QuotaWindow
    {
        public string Label { get; set; }
        public double? RemainingPercent { get; set; }
        public int? WindowMinutes { get; set; }
        public DateTime? ResetAt { get; set; }
        public string Credits { get; set; }
    }

    public sealed class QuotaInfo
    {
        public QuotaInfo() { Windows = new List<QuotaWindow>(); }
        public List<QuotaWindow> Windows { get; set; }
        public string Summary { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsStale { get; set; }
        public string Error { get; set; }
    }

    // All I/O runs off the UI thread. Only public read methods are called on Codex;
    // this class never opens authentication files or starts an inference turn.
    public sealed partial class AppServices : IDisposable
    {
        public const double ZhujiLatitude = 29.71877;
        public const double ZhujiLongitude = 120.24233;
        public const string WeatherEndpoint = "https://api.open-meteo.com/v1/forecast?latitude=29.71877&longitude=120.24233&current=temperature_2m,is_day,precipitation,rain,showers,snowfall,weather_code,wind_speed_10m,wind_gusts_10m&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm&timeformat=unixtime&timezone=Asia%2FShanghai&forecast_days=1";
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string applicationDirectory;
        private readonly object stateLock = new object();
        private readonly object weatherLock = new object();
        private readonly object quotaLock = new object();
        private readonly object hardwareLock = new object();
        private readonly HashSet<Process> children = new HashSet<Process>();
        private readonly HashSet<HttpWebRequest> requests = new HashSet<HttpWebRequest>();
        private volatile bool disposed;
        private WeatherInfo lastWeather;
        private WeatherLocationData weatherLocation = WeatherLocationData.DefaultZhuji;
        private long weatherLocationVersion;
        private QuotaInfo lastQuota;
        private DateTime cpuRetryAtUtc;
        private DateTime aidaWmiRetryAtUtc;
        private DateTime asusCpuRetryAtUtc;
        private DateTime asusCpuStartedAtUtc;
        private Task<HardwareInfo> asusCpuTask;
        private readonly bool asusSystem;
        private string asusCpuError;
        private string cpuNamespace;

        public AppServices() : this(AppDomain.CurrentDomain.BaseDirectory) { }
        public AppServices(string applicationDirectory)
        {
            this.applicationDirectory = applicationDirectory;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            try {
                using (var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS", false))
                    asusSystem = bios != null && Convert.ToString(bios.GetValue("SystemManufacturer"), CultureInfo.InvariantCulture).IndexOf("ASUS", StringComparison.OrdinalIgnoreCase) >= 0;
            } catch (Exception) { }
            // A bundled public quota snapshot is a dated cache, never a live read.
            try {
                string path = Path.Combine(applicationDirectory, "quota-snapshot.json");
                if (File.Exists(path) && new FileInfo(path).Length <= 1048576)
                    lastQuota = ParseQuotaSnapshotJson(File.ReadAllText(path));
            } catch (Exception) { }
        }

        public Task<HardwareInfo> ReadHardwareAsync()
        { return QueueQuery(hardwareLock, ReadHardware); }
        public Task<WeatherInfo> ReadWeatherAsync()
        { return QueueQuery(weatherLock, ReadWeather); }
        public Task<QuotaInfo> ReadQuotaAsync()
        { return QueueQuery(quotaLock, ReadQuota); }

        public WeatherLocationData CurrentWeatherLocation
        { get { lock (stateLock) { return weatherLocation.Copy(); } } }
        public long WeatherLocationVersion
        { get { lock (stateLock) { return weatherLocationVersion; } } }
        public void SetWeatherLocation(WeatherLocationData location)
        {
            if (location == null) throw new ArgumentNullException("location");
            WeatherLocationData value = location.ValidatedCopy();
            lock (stateLock) {
                ThrowIfDisposed();
                if (weatherLocation.Latitude == value.Latitude && weatherLocation.Longitude == value.Longitude
                    && weatherLocation.Name == value.Name && weatherLocation.Admin1 == value.Admin1
                    && weatherLocation.Admin2 == value.Admin2 && weatherLocation.Country == value.Country) return;
                weatherLocation = value; weatherLocationVersion++; lastWeather = null;
            }
        }

        public static string BuildWeatherEndpoint(WeatherLocationData location)
        {
            if (location == null) throw new ArgumentNullException("location");
            WeatherLocationData value = location.ValidatedCopy();
            return "https://api.open-meteo.com/v1/forecast?latitude=" + value.Latitude.ToString("R", CultureInfo.InvariantCulture)
                + "&longitude=" + value.Longitude.ToString("R", CultureInfo.InvariantCulture)
                + "&current=temperature_2m,is_day,precipitation,rain,showers,snowfall,weather_code,wind_speed_10m,wind_gusts_10m"
                + "&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm&timeformat=unixtime&timezone=auto&forecast_days=1";
        }

        private WeatherInfo ReadWeather()
        {
            WeatherLocationData location;
            long version;
            lock (stateLock) { location = weatherLocation.Copy(); version = weatherLocationVersion; }
            try
            {
                ThrowIfQueryStopped();
                var value = ParseWeatherJson(DownloadJson(BuildWeatherEndpoint(location)), location);
                if (!value.ObservedAt.HasValue || !value.WeatherCode.HasValue)
                    throw new InvalidDataException("Missing current weather data");
                value.LocationVersion = version;
                lock (stateLock) {
                    if (version != weatherLocationVersion) return CurrentWeatherFallback();
                    ThrowIfQueryStopped();
                    lastWeather = value;
                    return CloneWeather(value);
                }
            }
            catch (Exception)
            {
                lock (stateLock) { return CurrentWeatherFallback(); }
            }
        }

        // Caller holds stateLock; caches never cross between selected coordinates.
        private WeatherInfo CurrentWeatherFallback()
        {
            var value = lastWeather == null ? new WeatherInfo { Condition = "sun", Severity = 1,
                Summary = weatherLocation.Name + " · 天气待联网", UpdatedAt = DateTime.MinValue,
                LocationVersion = weatherLocationVersion } : CloneWeather(lastWeather);
            value.IsStale = true;
            value.Error = "天气暂不可用；联网后自动刷新。";
            return value;
        }

        public static WeatherInfo ParseWeatherJson(string json)
        { return ParseWeatherJson(json, WeatherLocationData.DefaultZhuji); }

        public static WeatherInfo ParseWeatherJson(string json, WeatherLocationData location)
        {
            if (location == null) throw new ArgumentNullException("location");
            location = location.ValidatedCopy();
            var root = Deserialize(json);
            var current = ObjectAt(root, "current");
            if (current == null) throw new InvalidDataException("No current weather");
            double? interval = NumberAt(current, "interval");
            double rateFactor = interval.HasValue && interval.Value > 0 ? 3600.0 / interval.Value : 1.0;
            var info = new WeatherInfo {
                Temperature = NumberAt(current, "temperature_2m"),
                WindSpeed = NumberAt(current, "wind_speed_10m"), WindGust = NumberAt(current, "wind_gusts_10m"),
                Precipitation = Multiply(NumberAt(current, "precipitation"), rateFactor),
                Snowfall = Multiply(NumberAt(current, "snowfall"), rateFactor),
                WeatherCode = IntegerAt(current, "weather_code"),
                ObservedAt = TimestampAt(current, "time"), UpdatedAt = DateTime.UtcNow,
                IsDay = IntegerAt(current, "is_day").HasValue ? IntegerAt(current, "is_day").Value != 0 : (bool?)null
            };
            int code = info.WeatherCode.GetValueOrDefault(-1);
            double precipitation = info.Precipitation.GetValueOrDefault();
            double snowfall = info.Snowfall.GetValueOrDefault();
            double wind = Math.Max(info.WindSpeed.GetValueOrDefault(), info.WindGust.GetValueOrDefault());
            bool snowy = snowfall > 0 || new[] { 71, 73, 75, 77, 85, 86 }.Contains(code);
            bool rainy = precipitation > 0 || new[] { 51, 53, 55, 56, 57, 61, 63, 65, 66, 67, 80, 81, 82, 95, 96, 99 }.Contains(code);
            if (snowy) {
                info.Condition = "snow";
                info.Severity = snowfall >= 1.0 || code == 75 || code == 86 ? 3 : snowfall >= 0.3 || code == 73 ? 2 : 1;
            } else if (rainy) {
                info.Condition = "rain";
                info.Severity = precipitation >= 10 || code == 65 || code == 67 || code == 82 || code >= 95 ? 3 : precipitation >= 2.5 || code == 63 || code == 81 ? 2 : 1;
            } else if (wind >= 20) {
                info.Condition = "wind";
                info.Severity = wind >= 62 ? 3 : wind >= 39 ? 2 : 1;
            } else {
                // Four requested skins: dry/cloudy/fog/night retain actual text,
                // and use the calm 'sun' skin rather than claiming clear daylight.
                info.Condition = "sun";
                info.Severity = info.IsDay == true && info.Temperature >= 35 ? 3 : info.IsDay == true && info.Temperature >= 28 ? 2 : 1;
            }
            var parts = new List<string> { location.Name, WeatherText(code, info.IsDay) };
            if (info.Temperature.HasValue) parts.Add(info.Temperature.Value.ToString("0.#", CultureInfo.InvariantCulture) + "°C");
            if (info.Condition == "wind") parts.Add("风/阵风 " + wind.ToString("0.#", CultureInfo.InvariantCulture) + " km/h");
            if (info.Condition == "rain") parts.Add(precipitation.ToString("0.#", CultureInfo.InvariantCulture) + " mm/h");
            if (info.Condition == "snow") parts.Add(snowfall.ToString("0.#", CultureInfo.InvariantCulture) + " cm/h");
            info.IsStale = !info.ObservedAt.HasValue || DateTime.UtcNow - info.ObservedAt.Value > TimeSpan.FromMinutes(90)
                || info.ObservedAt.Value - DateTime.UtcNow > TimeSpan.FromMinutes(30);
            info.Summary = String.Join(" · ", parts);
            if (info.IsStale) info.Error = "天气模型时间较旧，等待下次刷新。";
            return info;
        }

        private HardwareInfo ReadHardware()
        {
            var value = new HardwareInfo { UpdatedAt = DateTime.UtcNow };
            if (QueryStopped()) { value.Error = "后台查询已停止"; return value; }
            var memory = new MemoryStatus();
            memory.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0) {
                value.MemoryTotalGiB = memory.TotalPhysical / 1073741824.0;
                value.MemoryUsedGiB = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
                value.MemoryPercent = 100.0 * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical;
            }
            GpuTemperatureReading gpu = gpuTemperature.Read(QueryStopped);
            value.GpuTemperature = gpu.Temperature; value.GpuName = gpu.Name; value.GpuSensorSource = gpu.Source;
            if (QueryStopped()) { value.Error = "后台查询已停止"; return value; }
            if (cpuReaderForTesting != null) cpuReaderForTesting(value); else ReadCpuTemperature(value);
            var missing = new List<string>();
            if (!value.CpuTemperature.HasValue) missing.Add(value.Error ?? "CPU 温度未连接：可右键查看数据来源与状态，或参考 sensors 文件夹中的连接说明。");
            if (!value.GpuTemperature.HasValue) missing.Add("GPU 温度不可用" + (String.IsNullOrEmpty(gpu.Error) ? "" : "（" + gpu.Error + "）"));
            if (!value.MemoryPercent.HasValue) missing.Add("物理内存读取失败");
            value.Error = missing.Count == 0 ? null : String.Join("；", missing);
            return value;
        }

        private void ReadCpuTemperature(HardwareInfo info)
        {
            ThrowIfQueryStopped();
            if (ReadAsusCpuTemperature(info)) { info.Error = null; return; }
            ThrowIfQueryStopped();
            string asusError = info.Error;
            // Reuse an existing monitor's published readings before requesting a
            // second low-level sensor reader. These interfaces are read-only.
            if (ReadAida64Temperature(info)) { info.Error = null; return; }
            ThrowIfQueryStopped();
            if (!String.IsNullOrEmpty(asusError)) info.Error = asusError + " " + info.Error;
            string aidaError = info.Error;
            if (ReadCpuHelperSnapshot(info)) { info.Error = null; return; }
            ThrowIfQueryStopped();
            if (!String.IsNullOrEmpty(aidaError)) info.Error = aidaError;
            if (DateTime.UtcNow < cpuRetryAtUtc) return;
            var providers = cpuNamespace == null ? new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" } : new[] { cpuNamespace };
            foreach (string provider in providers) {
                ThrowIfQueryStopped();
                try {
                    var scope = new ManagementScope("\\\\.\\" + provider);
                    scope.Options.Timeout = TimeSpan.FromSeconds(2);
                    var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false };
                    using (var search = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Name, Value, Parent, Identifier FROM Sensor WHERE SensorType='Temperature'"), options))
                    using (var results = search.Get()) {
                        double? best = null; int bestRank = 0; string sensorName = null;
                        foreach (ManagementObject sensor in results) {
                            using (sensor) {
                                ThrowIfQueryStopped();
                                string name = Convert.ToString(sensor["Name"], CultureInfo.InvariantCulture);
                                string parent = Convert.ToString(sensor["Parent"], CultureInfo.InvariantCulture).ToLowerInvariant();
                                string identifier = Convert.ToString(sensor["Identifier"], CultureInfo.InvariantCulture).ToLowerInvariant();
                                if (!(parent.Contains("cpu") || identifier.Contains("cpu")) || parent.Contains("gpu") || identifier.Contains("gpu")) continue;
                                double temperature;
                                if (!Double.TryParse(Convert.ToString(sensor["Value"], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out temperature)
                                    || temperature < -20 || temperature > 150) continue;
                                string lower = name.ToLowerInvariant();
                                int rank = lower.Contains("package") || lower.Contains("tctl/tdie") || lower == "cpu (tctl/tdie)" ? 4
                                    : lower.Contains("tdie") || lower.Contains("tctl") ? 3 : lower.Contains("max") ? 2 : 1;
                                if (rank > bestRank || rank == bestRank && (!best.HasValue || temperature > best.Value)) {
                                    best = temperature; bestRank = rank; sensorName = name;
                                }
                            }
                        }
                        if (best.HasValue) {
                            info.CpuTemperature = best; info.CpuSensorSource = provider + " / " + sensorName;
                            cpuNamespace = provider; info.Error = null; return;
                        }
                    }
                } catch (OperationCanceledException) { throw; } catch (Exception) { }
            }
            ThrowIfQueryStopped();
            cpuNamespace = null;
            try {
                // Reads only an already-running provider. No server/listener or
                // driver is started by the pet; redirects and proxies are disabled.
                var selected = ParseCpuSensorJson(DownloadJson("http://127.0.0.1:8085/data.json", 1200));
                if (selected.CpuTemperature.HasValue) {
                    info.CpuTemperature = selected.CpuTemperature;
                    info.CpuSensorSource = selected.CpuSensorSource;
                    info.Error = null;
                    return;
                }
            } catch (Exception) { }
            cpuRetryAtUtc = DateTime.UtcNow.AddSeconds(30);
        }

        private bool ReadAsusCpuTemperature(HardwareInfo info)
        {
            ThrowIfQueryStopped();
            if (!asusSystem) return false;
            if (DateTime.UtcNow < asusCpuRetryAtUtc) { info.Error = asusCpuError; return false; }
            if (asusCpuTask != null && (asusCpuTask.IsCanceled || asusCpuTask.IsFaulted)) {
                if (asusCpuTask.IsFaulted) { var observed = asusCpuTask.Exception; }
                asusCpuTask = null;
            }
            if (asusCpuTask == null) {
                asusCpuStartedAtUtc = DateTime.UtcNow;
                CancellationToken token = queryContext.Value;
                asusCpuTask = Task.Run(delegate {
                    token.ThrowIfCancellationRequested(); var reading = QueryAsusCpuTemperature();
                    token.ThrowIfCancellationRequested(); return reading;
                }, token);
            }
            // Only one native read may be in flight. A blocked vendor driver
            // cannot freeze the UI or accumulate a new worker on every poll.
            int remaining = (int)Math.Max(0, 1200 - (DateTime.UtcNow - asusCpuStartedAtUtc).TotalMilliseconds);
            bool completed;
            try { completed = asusCpuTask.Wait(remaining, queryContext.Value); }
            catch (AggregateException) { asusCpuTask = null; ThrowIfQueryStopped(); info.Error = "ASUS 温度查询已取消，稍后重新读取。"; return false; }
            ThrowIfQueryStopped();
            if (!completed) {
                info.Error = "ASUS CPU 温度查询超时；正在尝试其他已连接的传感器。"; return false;
            }
            var value = asusCpuTask.Result; asusCpuTask = null;
            if (value.CpuTemperature.HasValue && DateTime.UtcNow - value.UpdatedAt <= TimeSpan.FromSeconds(20)) {
                info.CpuTemperature = value.CpuTemperature; info.CpuSensorSource = value.CpuSensorSource; return true;
            }
            asusCpuError = value.Error ?? "ASUS CPU 温度读数已过期。";
            asusCpuRetryAtUtc = DateTime.UtcNow.AddSeconds(30);
            info.Error = asusCpuError; return false;
        }

        // This query has no generic method/endpoint parameter: the only supported
        // operation is DSTS (get status) for the measured CPU-temperature endpoint.
        private static HardwareInfo QueryAsusCpuTemperature()
        {
            var info = new HardwareInfo();
            try {
                // The IOCTL is FILE_ANY_ACCESS. Request read access only and share
                // the existing ASUS device with Armoury Crate/ASUS services.
                using (var device = OpenAsusDevice(@"\\.\ATKACPI", 0x80000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero)) {
                    if (device.IsInvalid) {
                        int error = Marshal.GetLastWin32Error();
                        info.Error = error == 5 ? "ASUS CPU 温度接口访问被拒绝（Windows 错误 5）；请从普通桌面会话启动桌宠。"
                            : "ASUS CPU 温度接口暂不可见（Windows 错误 " + error + "）。";
                        return info;
                    }
                    byte[] output = new byte[16]; uint returned;
                    byte[] request = BuildAsusCpuTemperatureRequest();
                    if (!ReadAsusDevice(device, 0x0022240C, request, (uint)request.Length, output, (uint)output.Length, out returned, IntPtr.Zero)) {
                        info.Error = "ASUS CPU 温度查询失败（Windows 错误 " + Marshal.GetLastWin32Error() + "）。"; return info;
                    }
                    return ParseAsusCpuTemperatureResponse(output, (int)returned);
                }
            } catch (Exception) { info.Error = "ASUS CPU 温度接口暂不可读。"; return info; }
        }

        public static byte[] BuildAsusCpuTemperatureRequest()
        {
            // Little-endian wire record: method DSTS, argument byte count 8,
            // CPU temperature device ID, read selector 0. Never send DEVS/INIT.
            var request = new byte[16];
            BitConverter.GetBytes(0x53545344u).CopyTo(request, 0);
            BitConverter.GetBytes(8u).CopyTo(request, 4);
            BitConverter.GetBytes(0x00120094u).CopyTo(request, 8);
            return request;
        }
        public static HardwareInfo ParseAsusCpuTemperatureResponse(byte[] response, int returned)
        {
            var info = new HardwareInfo();
            if (response != null && returned >= 4 && returned <= response.Length && response.Length >= 4) {
                uint raw = BitConverter.ToUInt32(response, 0);
                // G-Helper subtracts the presence flag. Require that exact status
                // form and a plausible nonzero Celsius value; unsupported/error
                // sentinels and uninitialized zero must remain unavailable.
                if ((raw & 0xFFFF0000u) == 0x00010000u && (raw & 0xFFFFu) >= 1 && (raw & 0xFFFFu) <= 150) {
                    info.CpuTemperature = raw & 0xFFFFu;
                    info.CpuSensorSource = "ASUS ACPI / CPU 温度 (0x00120094)";
                    info.UpdatedAt = DateTime.UtcNow; return info;
                }
            }
            info.Error = "ASUS 驱动未返回有效的 CPU 温度；可能不支持此温度端点或读数尚未就绪。";
            return info;
        }
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle OpenAsusDevice(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadAsusDevice(SafeFileHandle device, uint control, byte[] input, uint inputLength, byte[] output, uint outputLength, out uint returned, IntPtr overlapped);

        public const string Aida64SetupInstructions = "AIDA64：文件→设置→硬件监测→外部程序，启用共享内存，并勾选 CPU Package（或 CPU 核心温度）、日期和时间；外部程序更新频率设为 1–5 秒，保持 AIDA64 运行。";

        private bool ReadAida64Temperature(HardwareInfo info)
        {
            ThrowIfQueryStopped();
            string error = null;
            try {
                // OpenExisting never creates a publisher or modifies its memory.
                // Dispose every poll so our reader cannot preserve an orphan mapping.
                using (var memory = MemoryMappedFile.OpenExisting("AIDA64_SensorValues", MemoryMappedFileRights.Read))
                using (var view = memory.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)) {
                    int length = (int)Math.Min(view.Capacity, 1048576);
                    byte[] before = new byte[length], after = new byte[length];
                    for (int attempt = 0; attempt < 3; attempt++) {
                        ThrowIfQueryStopped();
                        view.ReadArray(0, before, 0, length);
                        view.ReadArray(0, after, 0, length);
                        if (!before.SequenceEqual(after)) { Thread.Sleep(5); continue; }
                        var value = ParseAida64SensorXml(DecodeAida64Buffer(after), DateTime.Now);
                        if (value.CpuTemperature.HasValue) {
                            info.CpuTemperature = value.CpuTemperature; info.CpuSensorSource = value.CpuSensorSource; return true;
                        }
                        error = value.Error; break;
                    }
                    if (error == null) error = "AIDA64 导出正在更新，稍后重试。";
                }
            } catch (OperationCanceledException) { throw; }
            catch (FileNotFoundException) { }
            catch (UnauthorizedAccessException) { error = "AIDA64 共享内存访问被拒绝；请让 AIDA64 和桌宠在同一用户会话运行。"; }
            catch (Exception) { error = "AIDA64 共享内存暂不可读。"; }
            // AIDA's official registry exporter is a second read-only route. Read
            // only SensorValues, never product settings, registration or license data.
            ThrowIfQueryStopped();
            try {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\FinalWire\AIDA64\SensorValues", false)) {
                    if (key != null) {
                        var document = new XmlDocument { XmlResolver = null };
                        var root = document.CreateElement("aida"); document.AppendChild(root);
                        foreach (string name in key.GetValueNames()) {
                            ThrowIfQueryStopped();
                            if (!name.StartsWith("Value.", StringComparison.OrdinalIgnoreCase)) continue;
                            string id = name.Substring(6).ToUpperInvariant();
                            if (id != "STIME" && id != "SDATE" && AidaCpuRank(id) == 0) continue;
                            var record = document.CreateElement(id == "STIME" || id == "SDATE" ? "sys" : "temp");
                            foreach (var field in new[] { new[] { "id", id }, new[] { "label", Convert.ToString(key.GetValue("Label." + id), CultureInfo.InvariantCulture) },
                                new[] { "value", Convert.ToString(key.GetValue(name), CultureInfo.InvariantCulture) } }) {
                                var child = document.CreateElement(field[0]); child.InnerText = field[1] ?? ""; record.AppendChild(child);
                            }
                            root.AppendChild(record);
                        }
                        var value = ParseAida64SensorXml(document.OuterXml, DateTime.Now);
                        if (value.CpuTemperature.HasValue) {
                            info.CpuTemperature = value.CpuTemperature;
                            info.CpuSensorSource = value.CpuSensorSource.Replace("共享内存", "注册表导出"); return true;
                        }
                        error = value.Error;
                    }
                }
            } catch (OperationCanceledException) { throw; } catch (Exception) { }
            // Existing AIDA WMI export uses the documented root\WMI class.
            // Discover its actual properties rather than assuming an installed
            // version's complete schema. Missing exporters retry every 30 seconds.
            ThrowIfQueryStopped();
            if (DateTime.UtcNow >= aidaWmiRetryAtUtc) {
                aidaWmiRetryAtUtc = DateTime.UtcNow.AddSeconds(30);
                try {
                    var scope = new ManagementScope(@"\\.\root\WMI"); scope.Options.Timeout = TimeSpan.FromSeconds(2);
                    var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false };
                    using (var search = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM AIDA64_SensorValues"), options))
                    using (var results = search.Get()) {
                        var document = new XmlDocument { XmlResolver = null };
                        var root = document.CreateElement("aida"); document.AppendChild(root);
                        foreach (ManagementObject sensor in results) {
                            using (sensor) {
                                ThrowIfQueryStopped();
                                string id = AidaWmiField(sensor, "ID").ToUpperInvariant();
                                if (id != "SDATE" && id != "STIME" && AidaCpuRank(id) == 0) continue;
                                var record = document.CreateElement(id == "STIME" || id == "SDATE" ? "sys" : "temp");
                                foreach (var field in new[] { new[] { "id", id }, new[] { "label", AidaWmiField(sensor, "Label") }, new[] { "value", AidaWmiField(sensor, "Value") } }) {
                                    var child = document.CreateElement(field[0]); child.InnerText = field[1]; record.AppendChild(child);
                                }
                                root.AppendChild(record);
                            }
                        }
                        if (root.HasChildNodes) {
                            var value = ParseAida64SensorXml(document.OuterXml, DateTime.Now);
                            if (value.CpuTemperature.HasValue) {
                                info.CpuTemperature = value.CpuTemperature;
                                info.CpuSensorSource = value.CpuSensorSource.Replace("共享内存", "WMI 导出");
                                aidaWmiRetryAtUtc = DateTime.MinValue; return true;
                            }
                            error = value.Error;
                        }
                    }
                } catch (OperationCanceledException) { throw; } catch (Exception) { }
            }
            info.Error = (error == null ? "尚未连接 AIDA64 温度导出。" : error) + " " + Aida64SetupInstructions;
            return false;
        }
        private static string AidaWmiField(ManagementObject sensor, string name)
        {
            foreach (PropertyData property in sensor.Properties)
                if (String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? "";
            return "";
        }

        public static string DecodeAida64Buffer(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 2 || bytes.Length > 1048576) throw new InvalidDataException("AIDA buffer size invalid");
            // AIDA64 7.99 changed ANSI to Unicode. Honor BOMs and detect the
            // exporter ASCII '<' prefix stored in UTF-16; support older ANSI too.
            bool big = bytes[0] == 0xFE && bytes[1] == 0xFF;
            bool wide = big || bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[1] == 0;
            int step = wide ? 2 : 1, start = wide && (big || bytes[0] == 0xFF && bytes[1] == 0xFE) ? 2 : 0;
            for (int end = start; end + step <= bytes.Length; end += step) {
                if (bytes[end] != 0 || wide && bytes[end + 1] != 0) continue;
                return (wide ? big ? Encoding.BigEndianUnicode : Encoding.Unicode : Encoding.Default).GetString(bytes, start, end - start);
            }
            throw new InvalidDataException("AIDA buffer not terminated");
        }

        public static HardwareInfo ParseAida64SensorXml(string fragment, DateTime localNow)
        {
            var info = new HardwareInfo();
            if (String.IsNullOrWhiteSpace(fragment) || fragment.Length > 1048576) {
                info.Error = "AIDA64 未导出传感器数据。"; return info;
            }
            try {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1048576 };
                var document = new XmlDocument { XmlResolver = null };
                using (var text = new StringReader("<aida>" + fragment + "</aida>"))
                using (var reader = XmlReader.Create(text, settings)) document.Load(reader);
                string time = null, date = null;
                foreach (XmlNode record in document.GetElementsByTagName("sys")) {
                    string id = AidaField(record, "id");
                    if (id == "STIME") time = AidaField(record, "value");
                    else if (id == "SDATE") date = AidaField(record, "value");
                }
                DateTime updated;
                if (!TryAidaTimestamp(date, time, localNow, out updated)) {
                    info.Error = String.IsNullOrEmpty(time) ? "AIDA64 已连接，但没有导出时间；请勾选“时间”（含秒）。" : "AIDA64 导出已停止更新或更新时间不明。";
                    return info;
                }
                int bestRank = 0;
                foreach (XmlNode record in document.GetElementsByTagName("temp")) {
                    string id = AidaField(record, "id").ToUpperInvariant();
                    int rank = AidaCpuRank(id); double temperature;
                    // Exact temperature IDs exclude GPU GT/ISP/System Agent, CPU
                    // watts, utilisation, motherboard and generic thermal zones.
                    if (rank == 0 || !Double.TryParse(AidaField(record, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out temperature)
                        || Double.IsNaN(temperature) || Double.IsInfinity(temperature) || temperature < -20 || temperature > 150) continue;
                    if (rank > bestRank || rank == bestRank && (!info.CpuTemperature.HasValue || temperature > info.CpuTemperature.Value)) {
                        bestRank = rank; info.CpuTemperature = temperature;
                        info.CpuSensorSource = "AIDA64 共享内存 / " + (AidaField(record, "label") ?? id) + " (" + id + ")";
                    }
                }
                info.UpdatedAt = updated.ToUniversalTime();
                if (!info.CpuTemperature.HasValue) info.Error = "AIDA64 已连接，但没有导出 CPU 温度；请勾选 CPU Package 或 CPU 核心温度。";
            } catch (XmlException) { info.Error = "AIDA64 导出正在更新或 XML 不完整，稍后重试。"; }
            return info;
        }
        private static string AidaField(XmlNode record, string name)
        { var field = record.SelectSingleNode(name); return field == null ? "" : field.InnerText.Trim(); }
        private static int AidaCpuRank(string id)
        {
            if (Regex.IsMatch(id, @"^TCPU(?:[1-4])?PKG$")) return 5;
            if (id == "TCPUTCTL") return 4;
            if (id == "TCPUIAC" || Regex.IsMatch(id, @"^TCC-\d+-\d+$")) return 3;
            if (id == "TCPUDIO") return 2;
            if (Regex.IsMatch(id, @"^TCPU(?:[1-4])?$")) return 1;
            return 0;
        }
        private static bool TryAidaTimestamp(string date, string time, DateTime localNow, out DateTime updated)
        {
            updated = DateTime.MinValue; DateTime parsedTime;
            // Require seconds, because minute-only STIMENS cannot prove a fresh
            // five-second panel update. Exported Time is AIDA's update heartbeat.
            if (String.IsNullOrEmpty(time) || !Regex.IsMatch(time, @"\d{1,2}:\d{2}:\d{2}")
                || !DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out parsedTime)
                    && !DateTime.TryParse(time, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out parsedTime)) return false;
            var dates = new List<DateTime>();
            if (String.IsNullOrEmpty(date)) dates.AddRange(new[] { localNow.Date.AddDays(-1), localNow.Date, localNow.Date.AddDays(1) });
            else {
                foreach (string format in new[] { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "M/d/yyyy", "d/M/yyyy", "dd.MM.yyyy", "yyyy年M月d日" }) {
                    DateTime parsedDate;
                    if (DateTime.TryParseExact(date, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate)) dates.Add(parsedDate.Date);
                }
                if (dates.Count == 0) { DateTime parsedDate; if (DateTime.TryParse(date, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedDate)) dates.Add(parsedDate.Date); }
            }
            foreach (DateTime candidateDate in dates) {
                DateTime candidate = DateTime.SpecifyKind(candidateDate + parsedTime.TimeOfDay, DateTimeKind.Local);
                TimeSpan age = localNow - candidate;
                if (age.TotalSeconds >= -5 && age.TotalSeconds <= 20) { updated = candidate; return true; }
            }
            return false;
        }

        private bool ReadCpuHelperSnapshot(HardwareInfo info)
        {
            ThrowIfQueryStopped();
            try {
                string snapshot = Path.Combine(applicationDirectory, "sensors", "cpu.json");
                if (!File.Exists(snapshot) || new FileInfo(snapshot).Length > 65536) return false;
                var value = ParseCpuHelperJson(File.ReadAllText(snapshot, Encoding.UTF8));
                if (value.UpdatedAt == DateTime.MinValue) return false;
                info.CpuTemperature = value.CpuTemperature;
                info.CpuSensorSource = value.CpuSensorSource;
                info.Error = value.Error;
                return value.CpuTemperature.HasValue;
            } catch (Exception) { return false; }
        }
        public static HardwareInfo ParseCpuHelperJson(string json)
        {
            var root = Deserialize(json);
            var info = new HardwareInfo(); DateTime updated;
            if (!DateTime.TryParse(TextAt(root, "UpdatedAtUtc"), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out updated)) return info;
            updated = updated.ToUniversalTime();
            TimeSpan age = DateTime.UtcNow - updated;
            if (age > TimeSpan.FromSeconds(20) || age < TimeSpan.FromSeconds(-5)) return info;
            info.UpdatedAt = updated;
            double? temperature = NumberAt(root, "CpuTemperature");
            if (String.Equals(TextAt(root, "Status"), "ok", StringComparison.Ordinal)
                && temperature.HasValue && temperature >= -20 && temperature <= 150) {
                info.CpuTemperature = temperature;
                info.CpuSensorSource = "本地传感器助手 / " + (TextAt(root, "Source") ?? "真实 CPU 温度传感器");
            } else {
                string message = TextAt(root, "Message");
                info.Error = String.IsNullOrEmpty(message) ? "CPU 传感器助手暂无读数" : message.Substring(0, Math.Min(300, message.Length));
                if (TextAt(root, "Status") == "missing_driver") info.Error += " https://pawnio.eu/";
            }
            return info;
        }

        public static HardwareInfo ParseCpuSensorJson(string json)
        {
            var info = new HardwareInfo { UpdatedAt = DateTime.UtcNow };
            int bestRank = 0;
            FindCpuInTree(Deserialize(json), "", info, ref bestRank, 0);
            return info;
        }
        private static void FindCpuInTree(Dictionary<string, object> node, string parentHardware,
            HardwareInfo info, ref int bestRank, int depth)
        {
            if (node == null || depth > 20) return;
            string hardware = TextAt(node, "HardwareId") ?? parentHardware;
            string sensorId = TextAt(node, "SensorId") ?? "";
            string sensorIdentity = (hardware + " " + sensorId).ToLowerInvariant();
            if (String.Equals(TextAt(node, "Type"), "Temperature", StringComparison.OrdinalIgnoreCase)
                && sensorIdentity.Contains("cpu") && !sensorIdentity.Contains("gpu")) {
                string text = TextAt(node, "RawValue");
                // v0.9.6 formats RawValue using the sensor's Celsius format;
                // later builds may send a numeric value. Never use displayed F.
                double temperature;
                bool parsed = Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out temperature);
                if (!parsed && text != null && (text.Contains("°C") || text.Contains("℃"))) {
                    Match match = Regex.Match(text, @"[-+]?\d+(?:[.,]\d+)?");
                    parsed = match.Success && Double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out temperature);
                }
                if (parsed && temperature >= -20 && temperature <= 150) {
                    string name = TextAt(node, "Text") ?? sensorId;
                    string lower = name.ToLowerInvariant();
                    int rank = lower.Contains("package") || lower.Contains("tctl/tdie") ? 4
                        : lower.Contains("tdie") || lower.Contains("tctl") ? 3 : lower.Contains("max") ? 2 : 1;
                    if (rank > bestRank || rank == bestRank && (!info.CpuTemperature.HasValue || temperature > info.CpuTemperature.Value)) {
                        bestRank = rank; info.CpuTemperature = temperature;
                        info.CpuSensorSource = "LibreHardwareMonitor HTTP / " + name + " (" + sensorId + ")";
                    }
                }
            }
            object childrenValue;
            if (node.TryGetValue("Children", out childrenValue)) {
                var descendants = childrenValue as object[];
                if (descendants != null) foreach (object descendant in descendants)
                    FindCpuInTree(descendant as Dictionary<string, object>, hardware, info, ref bestRank, depth + 1);
            }
        }

        private QuotaInfo ReadQuota()
        {
            try {
                ThrowIfQueryStopped();
                string codex = FindCodexExecutable();
                if (codex == null) throw new FileNotFoundException("Codex not found");
                var lines = new BlockingCollection<string>();
                // Keep the helper's databases/log store away from the user's
                // existing conversations. Authentication still belongs to Codex.
                string runtimeRoot = Path.GetFullPath(Path.Combine(applicationDirectory, "runtime"));
                string runtime = Path.Combine(runtimeRoot, "quota-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(runtime);
                var commandSerializer = new JavaScriptSerializer();
                string arguments = "app-server --listen stdio:// -c "
                    + QuoteWindowsArgument("sqlite_home=" + commandSerializer.Serialize(runtime))
                    + " -c " + QuoteWindowsArgument("log_dir=" + commandSerializer.Serialize(runtime));
                var process = NewHiddenProcess(codex, arguments, true);
                try {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                        if (e.Data != null) { try { lines.Add(e.Data); } catch (InvalidOperationException) { } }
                    };
                    process.ErrorDataReceived += delegate { }; // Drain without persisting logs or authentication data.
                    StartTracked(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    var serializer = new JavaScriptSerializer();
                    process.StandardInput.WriteLine(serializer.Serialize(new { method = "initialize", id = 0,
                        @params = new { clientInfo = new { name = "xiaojing_desktop_pet", title = "XiaoJing Desktop Pet", version = "3.0.0" } } }));
                    process.StandardInput.Flush();
                    var deadline = DateTime.UtcNow.AddSeconds(18);
                    ReadRpcResponse(lines, 0, deadline);
                    process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}");
                    process.StandardInput.WriteLine("{\"method\":\"account/rateLimits/read\",\"id\":1}");
                    process.StandardInput.Flush();
                    var response = ReadRpcResponse(lines, 1, deadline);
                    var result = ObjectAt(response, "result");
                    if (result == null) throw new InvalidDataException("No quota response");
                    var value = ParseQuotaResult(result);
                    if (value.Windows.Count == 0) throw new InvalidDataException("No quota windows");
                    ThrowIfQueryStopped();
                    lastQuota = value; return CloneQuota(value, false);
                } finally {
                    StopTracked(process); lines.Dispose();
                    // Delete only this helper's fresh GUID directory; its transient
                    // diagnostics are not retained and no auth file is in this tree.
                    try {
                        if (Path.GetFullPath(runtime).StartsWith(runtimeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            Directory.Delete(runtime, true);
                    } catch (Exception) { }
                }
            } catch (Exception) {
                var value = lastQuota == null ? new QuotaInfo { UpdatedAt = DateTime.MinValue } : CloneQuota(lastQuota, true);
                value.IsStale = true;
                value.Error = "额度暂不可用；请确认当前 Codex 已登录并能联网。";
                value.Summary = value.Windows.Count > 0 ? "缓存额度，等待重试" : "Codex 额度待刷新";
                return value;
            }
        }

        public static QuotaInfo ParseQuotaSnapshotJson(string json)
        {
            var root = Deserialize(json);
            string captured = TextAt(root, "capturedAtUtc");
            DateTimeOffset timestamp;
            // Reject an absent/ambiguous clock rather than stamping cached data as now.
            if (String.IsNullOrWhiteSpace(captured)
                || !Regex.IsMatch(captured, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase)
                || !DateTimeOffset.TryParse(captured, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp)
                || timestamp.UtcDateTime > DateTime.UtcNow.AddMinutes(5)) return null;
            var value = ParseQuotaResult(root);
            if (value.Windows.Count == 0) return null;
            value.UpdatedAt = timestamp.UtcDateTime;
            value.IsStale = true;
            value.Summary = "缓存额度，等待重试";
            return value;
        }

        public static QuotaInfo ParseQuotaJson(string json)
        {
            var root = Deserialize(json);
            return ParseQuotaResult(ObjectAt(root, "result") ?? root);
        }

        private static QuotaInfo ParseQuotaResult(Dictionary<string, object> result)
        {
            var value = new QuotaInfo { UpdatedAt = DateTime.UtcNow, Summary = "当前 Codex 账户额度" };
            var buckets = ObjectAt(result, "rateLimitsByLimitId");
            if (buckets != null && buckets.Count > 0) {
                foreach (var bucket in buckets.OrderBy(pair => pair.Key == "codex" ? "" : pair.Key, StringComparer.Ordinal))
                    AddQuotaBucket(value, bucket.Key, bucket.Value as Dictionary<string, object>);
            } else AddQuotaBucket(value, "codex", ObjectAt(result, "rateLimits"));
            return value;
        }

        private static void AddQuotaBucket(QuotaInfo info, string key, Dictionary<string, object> bucket)
        {
            if (bucket == null) return;
            string bucketLabel = TextAt(bucket, "limitName") ?? TextAt(bucket, "limitId") ?? key;
            foreach (string kind in new[] { "primary", "secondary" }) {
                var source = ObjectAt(bucket, kind);
                if (source == null) continue;
                double? used = NumberAt(source, "usedPercent");
                int? minutes = IntegerAt(source, "windowDurationMins");
                DateTime? reset = TimestampAt(source, "resetsAt");
                string duration = minutes.HasValue ? DurationLabel(minutes.Value) : kind == "primary" ? "主要窗口" : "次要窗口";
                var window = new QuotaWindow {
                    Label = bucketLabel + " · " + duration,
                    RemainingPercent = used.HasValue ? Math.Max(0, Math.Min(100, 100 - used.Value)) : (double?)null,
                    WindowMinutes = minutes, ResetAt = reset
                };
                // A reset timestamp is not evidence that a new quota has arrived.
                if (reset.HasValue && reset.Value <= DateTime.UtcNow) { window.RemainingPercent = null; info.IsStale = true; }
                var credits = ObjectAt(bucket, "credits");
                if (credits != null) {
                    double? balance = NumberAt(credits, "balance");
                    window.Credits = balance.HasValue ? balance.Value.ToString("0.##", CultureInfo.InvariantCulture) : null;
                }
                info.Windows.Add(window);
            }
        }

        private Dictionary<string, object> ReadRpcResponse(BlockingCollection<string> lines, int id, DateTime deadline)
        {
            while (DateTime.UtcNow < deadline) {
                ThrowIfQueryStopped(); string line;
                int remaining = (int)Math.Max(1, Math.Min(500, (deadline - DateTime.UtcNow).TotalMilliseconds));
                if (!lines.TryTake(out line, remaining)) continue;
                Dictionary<string, object> message;
                try { message = Deserialize(line); } catch (Exception) { continue; }
                if (IntegerAt(message, "id") != id) continue;
                if (ObjectAt(message, "error") != null) throw new InvalidOperationException("Quota RPC failed");
                return message;
            }
            throw new TimeoutException("Quota RPC timed out");
        }

        private string DownloadJson(string url, int timeoutMs = 12000)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = timeoutMs; request.ReadWriteTimeout = timeoutMs;
            if (new Uri(url).IsLoopback) { request.Proxy = null; request.AllowAutoRedirect = false; }
            request.UserAgent = "XiaoJingDesktopPet/3.0 (personal desktop weather companion)";
            // Identity avoids .NET Framework's incompatible handling of some
            // proxy/server deflate streams; this small response needs no compression.
            request.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
            lock (stateLock) { ThrowIfQueryStopped(); requests.Add(request); }
            try {
                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                    string json = reader.ReadToEnd();
                    if (json.Length > 1024 * 1024) throw new InvalidDataException("Weather response too large");
                    return json;
                }
            } finally { lock (stateLock) { requests.Remove(request); } }
        }

        private Process NewHiddenProcess(string exe, string arguments, bool redirectInput)
        {
            return new Process { StartInfo = new ProcessStartInfo {
                FileName = exe, Arguments = arguments, WorkingDirectory = applicationDirectory,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = redirectInput,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            } };
        }
        private void StartTracked(Process process)
        { lock (stateLock) { ThrowIfQueryStopped(); process.Start(); children.Add(process); } }
        private void StopTracked(Process process)
        {
            lock (stateLock) { children.Remove(process); }
            try { if (!process.HasExited) { if (process.StartInfo.RedirectStandardInput) process.StandardInput.Close();
                if (!process.WaitForExit(350)) { process.Kill(); process.WaitForExit(1000); } } } catch (Exception) { }
            process.Dispose();
        }
        public void Dispose()
        {
            PauseBackgroundQueries(systemShutdown); gpuTemperature.Stop();
            List<Process> active; List<HttpWebRequest> activeRequests;
            lock (stateLock) { if (disposed) return; disposed = true; active = children.ToList(); activeRequests = requests.ToList(); }
            foreach (var request in activeRequests) { try { request.Abort(); } catch (Exception) { } }
            foreach (var process in active) { if (systemShutdown) StopQueryProcessWithoutWaiting(process); else StopTracked(process); }
            // The quota worker owns deletion of its fresh GUID directory. Let its
            // finally finish before the form exits; never wait while holding stateLock.
            // A canceled RPC checks disposed every 500 ms, after its child is stopped.
            bool quotaFinished = false;
            try { if (!systemShutdown) quotaFinished = Monitor.TryEnter(quotaLock, 4000); }
            finally { if (quotaFinished) Monitor.Exit(quotaLock); }
        }
        private void ThrowIfDisposed() { if (disposed) throw new ObjectDisposedException("AppServices"); }

        private static string FindExecutable(string name)
        {
            var locations = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name) };
            locations.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(p => !String.IsNullOrWhiteSpace(p)).Select(p => Path.Combine(p.Trim().Trim('"'), name)));
            return locations.FirstOrDefault(File.Exists);
        }
        private static string FindCodexExecutable()
        {
            string custom = Environment.GetEnvironmentVariable("XIAOJING_CODEX_PATH");
            if (!String.IsNullOrEmpty(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
            string fromPath = FindExecutable("codex.exe");
            if (fromPath != null) return fromPath;
            string bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(bin)) {
                try { return Directory.GetDirectories(bin).Select(d => Path.Combine(d, "codex.exe")).Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault(); } catch (Exception) { }
            }
            return null;
        }
        private static string QuoteWindowsArgument(string value)
        {
            // ProcessStartInfo uses the Windows argv parser, not a command shell.
            var quoted = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value) {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') { quoted.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                quoted.Append('\\', slashes).Append(character); slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static Dictionary<string, object> Deserialize(string json)
        { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        private static Dictionary<string, object> ObjectAt(Dictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null; }
        private static string TextAt(Dictionary<string, object> source, string key)
        { object value; return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null; }
        private static double? NumberAt(Dictionary<string, object> source, string key)
        { double value; return Double.TryParse(TextAt(source, key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !Double.IsNaN(value) && !Double.IsInfinity(value) ? value : (double?)null; }
        private static int? IntegerAt(Dictionary<string, object> source, string key)
        { double? value = NumberAt(source, key); return value.HasValue && value >= Int32.MinValue && value <= Int32.MaxValue ? (int)value.Value : (int?)null; }
        private static DateTime? TimestampAt(Dictionary<string, object> source, string key)
        { double? value = NumberAt(source, key); try { return value.HasValue && value > 0 ? UnixEpoch.AddSeconds(value.Value) : (DateTime?)null; } catch (ArgumentOutOfRangeException) { return null; } }
        private static double? Multiply(double? number, double factor) { return number.HasValue ? number.Value * factor : (double?)null; }
        private static string DurationLabel(int minutes)
        { return minutes == 10080 ? "每周" : minutes >= 1440 && minutes % 1440 == 0 ? (minutes / 1440) + " 天" : minutes >= 60 && minutes % 60 == 0 ? (minutes / 60) + " 小时" : minutes + " 分钟"; }
        private static string WeatherText(int code, bool? isDay)
        {
            if (code == 0) return isDay == false ? "晴朗夜间" : "晴天";
            if (code <= 3 && code >= 1) return code == 1 ? "大致晴朗" : code == 2 ? "多云" : "阴天";
            if (code == 45 || code == 48) return "雾";
            if (new[] { 71, 73, 75, 77, 85, 86 }.Contains(code)) return "降雪";
            if (code >= 95) return "雷雨";
            if (new[] { 56, 57, 66, 67 }.Contains(code)) return "冻雨";
            if (code >= 51 && code <= 82) return "降雨";
            return "天气模型";
        }
        private static WeatherInfo CloneWeather(WeatherInfo source)
        { return new WeatherInfo { Condition = source.Condition, Severity = source.Severity, Summary = source.Summary,
            Temperature = source.Temperature, WindSpeed = source.WindSpeed, WindGust = source.WindGust,
            Precipitation = source.Precipitation, Snowfall = source.Snowfall, WeatherCode = source.WeatherCode, IsDay = source.IsDay,
            ObservedAt = source.ObservedAt, UpdatedAt = source.UpdatedAt, IsStale = source.IsStale, Error = source.Error,
            LocationVersion = source.LocationVersion }; }
        private static QuotaInfo CloneQuota(QuotaInfo source, bool stale)
        { return new QuotaInfo { UpdatedAt = source.UpdatedAt, Summary = source.Summary, IsStale = stale || source.IsStale
            || source.Windows.Any(w => w.ResetAt.HasValue && w.ResetAt.Value <= DateTime.UtcNow),
            Error = source.Error, Windows = source.Windows.Select(w => new QuotaWindow { Label = w.Label,
                RemainingPercent = w.ResetAt.HasValue && w.ResetAt.Value <= DateTime.UtcNow ? null : w.RemainingPercent,
                ResetAt = w.ResetAt, WindowMinutes = w.WindowMinutes, Credits = w.Credits }).ToList() }; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length; public uint MemoryLoad; public ulong TotalPhysical; public ulong AvailablePhysical;
            public ulong TotalPageFile; public ulong AvailablePageFile; public ulong TotalVirtual; public ulong AvailableVirtual; public ulong AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    }
}


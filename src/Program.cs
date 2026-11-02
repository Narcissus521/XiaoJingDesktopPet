using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("XiaoJing Desktop Pet")]
[assembly: System.Reflection.AssemblyVersion("3.11.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.11.0.0")]

namespace XiaoJingDesktopPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string directory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (args.Length > 0 && args[0] == "--diagnose")
            {
                Diagnose(directory, args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(directory, "diagnostics"));
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\XiaoJingDesktopPet-20261008", out created))
            {
                if (!created)
                {
                    MessageBox.Show("小鲸已经在桌面上了。也可以在系统托盘里右键找到她。", "小鲸桌面宠物", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var services = new AppServices(directory))
                using (var form = new PetForm(services, directory))
                {
                    RunPetWithActivityMonitor(form);
                }
            }
        }

        private static void RunPetWithActivityMonitor(PetForm form)
        {
            using (var monitor = new CodexActivityMonitor())
            using (var timer = new System.Windows.Forms.Timer { Interval = 3000 })
            {
                bool reading = false, closing = false, resetPending = false;
                long epoch = 0;
                CancellationTokenSource readCancellation = new CancellationTokenSource();
                EventHandler refresh = async delegate {
                    if (reading || closing || form.BackgroundPollingSuspended) return;
                    reading = true; long startedEpoch = epoch; CancellationToken token = readCancellation.Token;
                    try { do {
                        bool reset = resetPending; resetPending = false;
                        CodexActivityInfo state = await monitor.ReadAsync(reset, token);
                        if (!closing && !form.IsDisposed && !form.BackgroundPollingSuspended && startedEpoch == epoch)
                            form.SetActivityState(state.IsWorking, state.IsDesktopOpen, state.Source + (String.IsNullOrWhiteSpace(state.Error) ? "" : "；" + state.Error));
                    } while (resetPending && !closing && !form.BackgroundPollingSuspended && startedEpoch == epoch);
                    } catch (OperationCanceledException) { }
                    catch (Exception) {
                        if (!closing && !form.IsDisposed && !form.BackgroundPollingSuspended && startedEpoch == epoch)
                            form.SetActivityState(null, null, "Codex 任务状态暂不可用");
                    } finally { reading = false; }
                };
                form.ActivityRefreshRequested += delegate { resetPending = true; refresh(form, EventArgs.Empty); };
                form.BackgroundPollingChanged += delegate {
                    epoch++; timer.Stop(); readCancellation.Cancel();
                    if (closing || form.BackgroundPollingSuspended) return;
                    readCancellation.Dispose(); readCancellation = new CancellationTokenSource(); resetPending = true;
                    timer.Start(); refresh(form, EventArgs.Empty);
                };
                timer.Tick += refresh;
                form.Shown += delegate { if (!form.BackgroundPollingSuspended) { timer.Start(); refresh(form, EventArgs.Empty); } };
                form.FormClosed += delegate { closing = true; timer.Stop(); readCancellation.Cancel(); };
                try { Application.Run(form); }
                finally { closing = true; timer.Stop(); readCancellation.Cancel(); readCancellation.Dispose(); }
            }
        }

        private static void Diagnose(string directory, string output)
        {
            Directory.CreateDirectory(output);
            string settings = Path.Combine(directory, "settings.ini");
            byte[] originalSettings = File.Exists(settings) ? File.ReadAllBytes(settings) : null;
            try {
            using (var services = new AppServices(directory))
            {
                var weatherTask = services.ReadWeatherAsync();
                var hardwareTask = services.ReadHardwareAsync();
                var quotaTask = services.ReadQuotaAsync();
                Task.WaitAll(weatherTask, hardwareTask, quotaTask);
                var weather = weatherTask.Result;
                var hardware = hardwareTask.Result;
                var quota = quotaTask.Result;
                using (var form = new PetForm(services, directory))
                {
                    form.DisableAutomaticPollingForPreview();
                    form.SetPreviewData(weather, quota, hardware);
                    form.SetPreviewMode(1);
                    form.SavePreview(Path.Combine(output, "account.png"));
                    form.SetPreviewMode(2);
                    form.SavePreview(Path.Combine(output, "hardware.png"));
                    foreach (string type in new[] { "sun", "wind", "rain", "snow" })
                        for (int severity = 1; severity <= 3; severity++)
                        {
                            form.SetPreviewWeather(type, severity);
                            form.SavePreview(Path.Combine(output, type + "-" + severity + ".png"));
                        }
                    // Test the real native layered window without moving the user's cursor.
                    var closeTimer = new System.Windows.Forms.Timer { Interval = 800 };
                    closeTimer.Tick += delegate {
                        closeTimer.Stop();
                        var report = new {
                            rendered = form.HasRenderedLayeredFrame,
                            renderError = form.LastRenderError,
                            weather = new { weather.Summary, weather.Condition, weather.Severity, weather.IsStale },
                            hardware = new { hardware.CpuTemperature, hardware.GpuTemperature,
                                hardware.MemoryPercent, hardware.MemoryUsedGiB, hardware.MemoryTotalGiB,
                                hardware.CpuSensorSource, hardware.GpuSensorSource, hardware.GpuName, hardware.Error },
                            quota = new { quota.Summary, quota.IsStale,
                                windows = quota.Windows.Select(w => new { w.Label, w.RemainingPercent, w.ResetAt }).ToArray() },
                            checkedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                        };
                        File.WriteAllText(Path.Combine(output, "diagnostic.json"), new JavaScriptSerializer().Serialize(report));
                        form.Close();
                    };
                    form.Shown += delegate { closeTimer.Start(); };
                    Application.Run(form);
                    closeTimer.Dispose();
                }
            }
            } finally {
                if (originalSettings != null) File.WriteAllBytes(settings, originalSettings);
                else if (File.Exists(settings)) File.Delete(settings);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private readonly Timer resumeRefreshTimer = new Timer { Interval = 500 };
        private long pollingEpoch;
        private bool resumeHardware, resumeWeather, resumeQuota;
        public bool BackgroundPollingSuspended { get { return closed || services.BackgroundQueriesPaused; } }
        public event EventHandler BackgroundPollingChanged;
        private void InitializePowerEvents()
        {
            SystemEvents.PowerModeChanged += SystemPowerModeChanged;
            SystemEvents.SessionEnding += SystemSessionEnding;
            resumeRefreshTimer.Tick += async delegate
            {
                if (BackgroundPollingSuspended) { resumeRefreshTimer.Stop(); return; }
                var tasks = new List<Task>();
                if (resumeHardware && !telemetryBusy) { resumeHardware = false; tasks.Add(RefreshHardwareAsync()); }
                if (resumeWeather && !weatherBusy) { resumeWeather = false; tasks.Add(RefreshWeatherAsync()); }
                if (resumeQuota && !quotaBusy) { resumeQuota = false; tasks.Add(RefreshQuotaAsync()); }
                if (!resumeHardware && !resumeWeather && !resumeQuota) resumeRefreshTimer.Stop();
                await Task.WhenAll(tasks);
            };
        }
        private void RemovePowerEvents()
        { SystemEvents.PowerModeChanged -= SystemPowerModeChanged; SystemEvents.SessionEnding -= SystemSessionEnding; }
        private void SystemPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) SuspendBackgroundPolling(false);
            else if (e.Mode == PowerModes.Resume) RunPowerActionOnUi(delegate { ResumeBackgroundPolling(true); });
        }
        private void SystemSessionEnding(object sender, SessionEndingEventArgs e) { SuspendBackgroundPolling(true); }
        private void RunPowerActionOnUi(Action action)
        {
            if (closed || IsDisposed) return;
            if (IsHandleCreated && InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
            else action();
        }
        private void SuspendBackgroundPolling(bool shuttingDown)
        {
            // Cancel I/O immediately on the system-event thread; UI timer work
            // follows on the form thread. No query helper is waited on here.
            services.PauseBackgroundQueries(shuttingDown);
            RunPowerActionOnUi(delegate
            {
                pollingEpoch++;
                PauseHomeMotion(animationClock.ElapsedMilliseconds);
                animationTimer.Stop(); telemetryTimer.Stop(); weatherTimer.Stop(); quotaTimer.Stop(); resumeRefreshTimer.Stop();
                resumeHardware = resumeWeather = resumeQuota = false;
                EventHandler handler = BackgroundPollingChanged; if (handler != null) handler(this, EventArgs.Empty);
                SaveSettings();
            });
        }
        private void ResumeBackgroundPolling(bool refreshImmediately)
        {
            if (closed || !services.BackgroundQueriesPaused) return;
            services.ResumeBackgroundQueries();
            if (services.BackgroundQueriesPaused) return;
            pollingEpoch++;
            ResumeHomeMotion(animationClock.ElapsedMilliseconds);
            EventHandler handler = BackgroundPollingChanged; if (handler != null) handler(this, EventArgs.Empty);
            if (ready) animationTimer.Start();
            if (diagnosticPreview) return;
            telemetryTimer.Start(); weatherTimer.Start(); quotaTimer.Start();
            if (refreshImmediately) { resumeHardware = resumeWeather = resumeQuota = true; resumeRefreshTimer.Start(); }
        }
        private bool AcceptQueryResult(long epoch) { return !BackgroundPollingSuspended && epoch == pollingEpoch; }
    }
}

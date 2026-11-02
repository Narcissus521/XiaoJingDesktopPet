using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoJingDesktopPet
{
    public sealed partial class AppServices
    {
        private readonly NvidiaGpuTemperature gpuTemperature = new NvidiaGpuTemperature();
        private readonly Action<HardwareInfo> cpuReaderForTesting;
        private readonly AsyncLocal<CancellationToken> queryContext = new AsyncLocal<CancellationToken>();
        private CancellationTokenSource queryCancellation = new CancellationTokenSource();
        private volatile bool queriesPaused, systemShutdown;
        public bool BackgroundQueriesPaused { get { return disposed || queriesPaused; } }
        internal AppServices(string directory, NvidiaGpuTemperature gpu, Action<HardwareInfo> cpuReader) : this(directory)
        { gpuTemperature = gpu; cpuReaderForTesting = cpuReader; }
        private Task<T> QueueQuery<T>(object gate, Func<T> read)
        {
            CancellationToken token;
            lock (stateLock)
            {
                if (disposed || queriesPaused) return Task.FromCanceled<T>(new CancellationToken(true));
                token = queryCancellation.Token;
            }
            return Task.Run(delegate
            {
                CancellationToken previous = queryContext.Value; queryContext.Value = token;
                try { lock (gate) { ThrowIfQueryStopped(); return read(); } }
                finally { queryContext.Value = previous; }
            }, token);
        }
        private bool QueryStopped() { return disposed || queriesPaused || queryContext.Value.IsCancellationRequested; }
        private void ThrowIfQueryStopped()
        {
            ThrowIfDisposed();
            queryContext.Value.ThrowIfCancellationRequested();
            if (queriesPaused) throw new OperationCanceledException("后台查询已暂停");
        }
        public void PauseBackgroundQueries(bool shuttingDown)
        {
            List<Process> active; List<HttpWebRequest> activeRequests; CancellationTokenSource cancellation;
            lock (stateLock)
            {
                if (shuttingDown) systemShutdown = true;
                if (disposed || queriesPaused) return;
                queriesPaused = true; cancellation = queryCancellation;
                // Serialize cancellation with Resume disposing the old source.
                // Only our internal worker tasks register this token.
                cancellation.Cancel();
                active = new List<Process>(children); activeRequests = new List<HttpWebRequest>(requests);
            }
            foreach (var request in activeRequests) { try { request.Abort(); } catch (Exception) { } }
            // Only our tracked public-query helpers are affected. Never wait for
            // them on a suspend/shutdown event, or touch the Codex desktop process.
            foreach (var process in active) StopQueryProcessWithoutWaiting(process);
        }
        public void ResumeBackgroundQueries()
        {
            CancellationTokenSource old;
            lock (stateLock)
            {
                if (disposed || systemShutdown || !queriesPaused) return;
                old = queryCancellation; queryCancellation = new CancellationTokenSource(); queriesPaused = false;
            }
            old.Dispose();
        }
        private static void StopQueryProcessWithoutWaiting(Process process)
        { try { if (!process.HasExited) process.Kill(); } catch (Exception) { } }
    }
}

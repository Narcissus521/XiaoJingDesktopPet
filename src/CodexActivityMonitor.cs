using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XiaoJingDesktopPet
{
    public sealed class CodexActivityInfo
    {
        public bool? IsWorking { get; set; }
        public bool? IsDesktopOpen { get; set; }
        public string Source { get; set; }
        public string Error { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string StatusLabel
        {
            get { return IsDesktopOpen == false ? "睡眠中" : IsWorking == true ? "工作中" : IsWorking == false && IsDesktopOpen == true ? "摸鱼中" : "状态未知"; }
        }
    }

    // Independent app-server processes do not share their in-memory active turns.
    // Observe the desktop's existing rollout lifecycle metadata plus its writer
    // locks instead. Never start/resume a thread, read auth or decode message text.
    public sealed class CodexActivityMonitor : IDisposable
    {
        private const string SourceName = "Codex Desktop 生命周期事件（含子任务）";
        private readonly string codexHome;
        private readonly Func<bool?> desktopPresence;
        private readonly object gate = new object();
        private readonly Dictionary<string, Cursor> cursors = new Dictionary<string, Cursor>(StringComparer.OrdinalIgnoreCase);
        private volatile bool disposed;
        private CancellationToken readCancellation;
        // Resuming a chat can append a runtime/session UUID to the rollout name.
        // The first UUID in that final underscore-separated chain owns the lock.
        private static readonly Regex ThreadFile = new Regex(@"^[^_]*?([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})*\.jsonl$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public CodexActivityMonitor() : this(DefaultHome()) { }
        public CodexActivityMonitor(string codexHome) : this(codexHome, ReadDesktopPresence) { }
        public CodexActivityMonitor(string codexHome, Func<bool?> desktopPresence)
        {
            if (desktopPresence == null) throw new ArgumentNullException("desktopPresence");
            this.codexHome = Path.GetFullPath(codexHome);
            this.desktopPresence = desktopPresence;
        }
        public Task<CodexActivityInfo> ReadAsync()
        { return ReadAsync(false); }
        public Task<CodexActivityInfo> ReadAsync(bool reset) { return ReadAsync(reset, CancellationToken.None); }
        public Task<CodexActivityInfo> ReadAsync(bool reset, CancellationToken cancellation)
        {
            return Task.Run(delegate { lock (gate) {
                readCancellation = cancellation;
                try {
                    cancellation.ThrowIfCancellationRequested();
                    if (reset) ClearCursors();
                    return Read();
                } finally { readCancellation = CancellationToken.None; if (disposed) ClearCursors(); }
            } }, cancellation);
        }

        private CodexActivityInfo Read()
        {
            var result = new CodexActivityInfo { Source = SourceName, UpdatedAt = DateTime.UtcNow };
            if (disposed) { result.Error = "状态监测已停止。"; return result; }
            try {
                readCancellation.ThrowIfCancellationRequested();
                result.IsDesktopOpen = desktopPresence();
                readCancellation.ThrowIfCancellationRequested();
                if (result.IsDesktopOpen == false) {
                    result.IsWorking = false; result.Source = "Codex 桌面程序未打开"; return result;
                }
                string sessions = Path.Combine(codexHome, "sessions"), locks = Path.Combine(codexHome, "thread-writer-locks");
                if (!Directory.Exists(sessions) || !Directory.Exists(locks)) {
                    result.Error = "没有可读取的本机 Codex 桌面任务事件。"; return result;
                }
                // Exactly one rollout generation can own a persistent chat's
                // writer. Historical syncing may touch older files later, so
                // prefer the generation's filename date, not last-write time.
                var files = Directory.GetFiles(sessions, "*.jsonl", SearchOption.AllDirectories)
                    .Select(path => RolloutFile.Create(path)).Where(file => file != null)
                    .GroupBy(file => file.ThreadId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(file => file.Generation)
                        .ThenByDescending(file => file.File.Name, StringComparer.OrdinalIgnoreCase).First())
                    .OrderByDescending(file => file.File.LastWriteTimeUtc).ToList();
                int active = 0; bool uncertain = false;
                var reasons = new HashSet<string>();
                var budget = Stopwatch.StartNew();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RolloutFile rollout in files) {
                    readCancellation.ThrowIfCancellationRequested();
                    if (disposed) break;
                    FileInfo file = rollout.File;
                    string thread = rollout.ThreadId;
                    string lockPath = Path.Combine(locks, thread + ".lock");
                    int ownership = WriterOwnership(lockPath);
                    if (ownership < 0) { uncertain = true; reasons.Add("部分任务写锁暂不可读"); continue; }
                    // Unowned history cannot represent a live turn. In particular,
                    // do not open every historical header: one locked/rotated old
                    // rollout must not discard a healthy executing task.
                    if (ownership == 0) continue;
                    try {
                    Cursor cursor;
                    if (!cursors.TryGetValue(file.FullName, out cursor)) {
                        cursor = new Cursor(); cursors.Add(file.FullName, cursor);
                    }
                    visited.Add(file.FullName);
                    if (!cursor.HeaderKnown) ReadHeader(file.FullName, cursor);
                    if (!cursor.HeaderKnown) { uncertain = true; reasons.Add("任务来源元数据仍在写入"); continue; }
                    if (!cursor.IsDesktop) continue; // Other CLI clients aren't desktop activity.
                    if (budget.ElapsedMilliseconds >= 1800) { uncertain = true; reasons.Add("任务事件仍在分段同步"); continue; }
                    Scan(file.FullName, cursor, budget);
                    if (!cursor.LifecycleAt.HasValue) {
                        // Merely opening a chat may create and own a rollout before
                        // any task has run. A complete, fully scanned desktop file
                        // with no start event is idle, not a failed status read.
                        // Still defer while catching up or receiving a partial line.
                        if (!cursor.CaughtUp || cursor.PrefixCount != 0) {
                            uncertain = true; reasons.Add("尚未读到完整任务生命周期");
                        }
                        continue;
                    }
                    // A newly acquired writer cannot authenticate an unfinished turn
                    // from a previous process. A fresh lifecycle event must follow it.
                    DateTime writerAt = File.GetLastWriteTimeUtc(lockPath);
                    if (cursor.IsWorking == true && cursor.LifecycleAt.Value < writerAt.AddSeconds(-5) || cursor.LifecycleAt.Value > DateTime.UtcNow.AddMinutes(1)) {
                        uncertain = true; reasons.Add("等待当前写入进程的新任务事件"); continue;
                    }
                    int stillOwned = WriterOwnership(lockPath);
                    if (stillOwned < 0) { uncertain = true; reasons.Add("任务写锁正在切换"); continue; }
                    if (stillOwned == 0) continue;
                    // Completed records are authoritative even if a following
                    // message is only half appended. Never decode partial JSON.
                    // While catching up, only a confirmed executing turn may win;
                    // an older idle event cannot establish the current idle state.
                    if (cursor.IsWorking == true) active++;
                    else if (!cursor.CaughtUp) { uncertain = true; reasons.Add("正在同步后续任务事件"); }
                    } catch (FileNotFoundException) { /* A concurrently archived file is no longer active. */ }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) { uncertain = true; reasons.Add("个别任务文件暂被占用"); }
                    catch (UnauthorizedAccessException) { uncertain = true; reasons.Add("个别任务文件暂不可读"); }
                }
                foreach (string old in cursors.Keys.Where(path => !visited.Contains(path)).ToArray()) cursors.Remove(old);
                if (disposed) { result.Error = "状态监测已停止。"; return result; }
                if (active > 0) {
                    result.IsWorking = true; result.IsDesktopOpen = true;
                    result.Source = SourceName + " · " + active + " 个执行中的任务";
                } else if (uncertain) {
                    result.Error = String.Join("；", reasons) + "；可右键立即刷新工作状态。";
                } else {
                    result.IsWorking = false;
                    result.Source = SourceName + " · 当前没有执行中的任务";
                }
            } catch (OperationCanceledException) { throw; }
            catch (Exception) { result.Error = "无法读取本机 Codex 任务生命周期；状态未知。"; }
            return result;
        }

        // The Windows Store Codex frontend is currently named ChatGPT.exe.
        // Match its package identity, not that name alone or a CLI app-server.
        private static bool IsDesktopExecutable(string path, string product)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            string normalized = path.Replace('/', '\\');
            if (Regex.IsMatch(normalized, @"\\WindowsApps\\OpenAI\.Codex_[^\\]+\\app\\(?:ChatGPT|Codex)\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            return String.Equals(Path.GetFileName(normalized), "Codex.exe", StringComparison.OrdinalIgnoreCase)
                && !String.IsNullOrWhiteSpace(product) && product.IndexOf("Codex", StringComparison.OrdinalIgnoreCase) >= 0
                && normalized.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool? ReadDesktopPresence()
        {
            bool uncertain = false;
            try {
                int session;
                using (Process current = Process.GetCurrentProcess()) session = current.SessionId;
                foreach (string name in new[] { "ChatGPT", "Codex" }) {
                    foreach (Process process in Process.GetProcessesByName(name)) {
                        using (process) {
                            try {
                                if (process.SessionId != session || process.HasExited) continue;
                                ProcessModule module = process.MainModule;
                                if (module == null) { uncertain = true; continue; }
                                if (IsDesktopExecutable(module.FileName, module.FileVersionInfo.ProductName)) return true;
                            } catch (InvalidOperationException) { /* Process exited during the query. */ }
                            catch (Exception) { uncertain = true; }
                        }
                    }
                }
                return uncertain ? (bool?)null : false;
            } catch (Exception) { return null; }
        }

        // A writer owns an exclusive file lock. Test sharing only, without reading
        // the lock's contents, locking bytes, deleting it or touching timestamps.
        private static int WriterOwnership(string path)
        {
            try {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return 0;
            } catch (FileNotFoundException) { return 0; }
            catch (DirectoryNotFoundException) { return 0; }
            catch (IOException ex) {
                int error = ex.HResult & 0xffff;
                return error == 32 || error == 33 ? 1 : -1;
            } catch (UnauthorizedAccessException) { return -1; }
        }
        private static void ReadHeader(string path, Cursor cursor)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                byte[] prefix = new byte[2048]; int count = file.Read(prefix, 0, prefix.Length);
                var metadata = PrefixMetadata.Parse(prefix, count);
                if (metadata.Originator == null) return;
                cursor.HeaderKnown = true;
                string origin = metadata.Originator.Replace(" ", "").Replace("_", "").ToLowerInvariant();
                cursor.IsDesktop = origin == "codexdesktop" || origin == "codexworkdesktop";
            }
        }
        private void Scan(string path, Cursor cursor, Stopwatch budget)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                if (file.Length < cursor.Position) cursor.Reset();
                file.Position = cursor.Position;
                byte[] buffer = new byte[65536];
                while (!disposed && budget.ElapsedMilliseconds < 1800) {
                    readCancellation.ThrowIfCancellationRequested();
                    int count = file.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    int begin = 0;
                    while (begin < count) {
                        readCancellation.ThrowIfCancellationRequested();
                        int end = Array.IndexOf(buffer, (byte)10, begin, count - begin);
                        int stop = end < 0 ? count : end;
                        int copy = Math.Min(stop - begin, cursor.Prefix.Length - cursor.PrefixCount);
                        if (copy > 0) { Buffer.BlockCopy(buffer, begin, cursor.Prefix, cursor.PrefixCount, copy); cursor.PrefixCount += copy; }
                        if (end >= 0) {
                            // Only event header fields are decoded. Message bodies,
                            // prompts, outputs and titles are skipped as raw bytes.
                            var metadata = PrefixMetadata.Parse(cursor.Prefix, cursor.PrefixCount);
                            if (metadata.At.HasValue && metadata.Kind != null) {
                                cursor.IsWorking = metadata.Kind == "task_started";
                                cursor.LifecycleAt = metadata.At;
                            }
                            Array.Clear(cursor.Prefix, 0, cursor.Prefix.Length);
                            cursor.PrefixCount = 0; begin = end + 1;
                        } else begin = count;
                    }
                    cursor.Position = file.Position;
                }
                cursor.CaughtUp = cursor.Position == file.Length;
            }
        }
        private static string DefaultHome()
        {
            string configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return String.IsNullOrWhiteSpace(configured) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : configured;
        }
        public void Dispose()
        {
            disposed = true;
            if (!Monitor.TryEnter(gate)) return; // The active worker cleans up in its finally.
            try { ClearCursors(); } finally { Monitor.Exit(gate); }
        }
        private void ClearCursors()
        { foreach (Cursor cursor in cursors.Values) Array.Clear(cursor.Prefix, 0, cursor.Prefix.Length); cursors.Clear(); }

        private sealed class RolloutFile
        {
            public FileInfo File;
            public string ThreadId;
            public DateTime Generation;
            public static RolloutFile Create(string path)
            {
                var file = new FileInfo(path);
                Match match = ThreadFile.Match(file.Name);
                if (!match.Success) return null;
                DateTime generation;
                if (!file.Name.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)
                    || file.Name.Length < 27
                    || !DateTime.TryParseExact(file.Name.Substring(8, 19), "yyyy-MM-dd'T'HH-mm-ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out generation))
                    generation = file.LastWriteTime;
                return new RolloutFile { File = file, ThreadId = match.Groups[1].Value, Generation = generation };
            }
        }

        private sealed class Cursor
        {
            public bool HeaderKnown, IsDesktop, CaughtUp;
            public bool? IsWorking;
            public DateTime? LifecycleAt;
            public long Position;
            public byte[] Prefix = new byte[2048];
            public int PrefixCount;
            public void Reset() { Position = 0; PrefixCount = 0; LifecycleAt = null; IsWorking = null; CaughtUp = false; }
        }

        // This deliberately isn't a general JSON deserializer. It retains only
        // timestamp/type/originator scalar metadata and stops before message data.
        private sealed class PrefixMetadata
        {
            public string Kind, Originator;
            public DateTime? At;
            public static PrefixMetadata Parse(byte[] bytes, int count)
            {
                var value = new PrefixMetadata();
                try {
                    var reader = new HeaderReader(bytes, count);
                    if (!reader.Take((byte)'{')) return value;
                    string outer = null, timestamp = null;
                    while (!reader.End) {
                        string key = reader.String(64);
                        if (key == null || !reader.Take((byte)':')) break;
                        if (key == "type") outer = reader.String(64);
                        else if (key == "timestamp") timestamp = reader.String(64);
                        else if (key == "payload") {
                            if (outer != "event_msg" && outer != "session_meta" || !reader.Take((byte)'{')) break;
                            while (!reader.End) {
                                string field = reader.String(64);
                                if (field == null || !reader.Take((byte)':')) break;
                                if (outer == "event_msg" && field == "type") {
                                    string kind = reader.String(64);
                                    if (kind == "task_started" || kind == "task_complete" || kind == "turn_aborted") {
                                        DateTimeOffset at;
                                        if (DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out at)) { value.Kind = kind; value.At = at.UtcDateTime; }
                                    }
                                    return value;
                                }
                                if (outer == "session_meta" && field == "originator") { value.Originator = reader.String(128); return value; }
                                if (!reader.SkipValue() || !reader.Take((byte)',')) break;
                            }
                            break;
                        } else if (!reader.SkipValue()) break;
                        if (!reader.Take((byte)',')) break;
                    }
                } catch (InvalidDataException) { }
                return value;
            }
        }
        private sealed class HeaderReader
        {
            private readonly byte[] bytes; private readonly int count; private int offset;
            public HeaderReader(byte[] bytes, int count) { this.bytes = bytes; this.count = count; }
            public bool End { get { return offset >= count; } }
            private void White() { while (!End && (bytes[offset] == 32 || bytes[offset] == 9 || bytes[offset] == 13 || bytes[offset] == 10)) offset++; }
            public bool Take(byte expected) { White(); if (End || bytes[offset] != expected) return false; offset++; return true; }
            public string String(int max)
            {
                White(); if (!Take((byte)'"')) return null;
                var text = new StringBuilder(); bool escape = false;
                while (!End) {
                    byte b = bytes[offset++];
                    if (!escape && b == '"') return text.ToString();
                    if (!escape && b == '\\') { escape = true; continue; }
                    if (b < 32 || b > 126 || text.Length >= max) throw new InvalidDataException();
                    text.Append((char)b); escape = false;
                }
                return null;
            }
            public bool SkipValue()
            {
                White(); int depth = 0; bool quoted = false, escape = false;
                while (!End) {
                    byte b = bytes[offset];
                    if (quoted) {
                        offset++;
                        if (escape) escape = false; else if (b == '\\') escape = true; else if (b == '"') quoted = false;
                        continue;
                    }
                    if (depth == 0 && (b == ',' || b == '}')) return true;
                    offset++;
                    if (b == '"') quoted = true;
                    else if (b == '{' || b == '[') depth++;
                    else if (b == '}' || b == ']') { if (--depth < 0) return false; }
                }
                return false;
            }
        }
    }
}

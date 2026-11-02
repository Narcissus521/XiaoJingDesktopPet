using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    // The whole window is one ARGB surface. This preserves the soft edges of the
    // illustration and makes fully transparent pixels pass mouse input through.
    public sealed partial class PetForm : Form
    {
        private const int BaseWidth = 272;
        private const int ChristmasWidth = 328;
        private const int BaseHeight = 350;
        private static readonly RectangleF LeftGiftBounds = new RectangleF(10, 250, 86, 65);
        private static readonly RectangleF RightGiftBounds = new RectangleF(185, 250, 86, 65);
        private static readonly string[] HolidayIds = new string[] { "springfestival", "lantern", "qingming", "dragonboat", "qixi", "midautumn", "chongyang", "laba", "halloween", "christmas" };
        private readonly AppServices services;
        private readonly string applicationDirectory;
        private readonly string settingsPath;
        private readonly Dictionary<string, Image> images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch animationClock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly Timer animationTimer = new Timer();
        private readonly Timer telemetryTimer = new Timer();
        private readonly Timer weatherTimer = new Timer();
        private readonly Timer quotaTimer = new Timer();
        private readonly ContextMenuStrip petMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem quotaModeItem = new ToolStripMenuItem("1  显示 Codex 剩余额度");
        private readonly ToolStripMenuItem telemetryModeItem = new ToolStripMenuItem("2  显示硬件状态");
        private readonly ToolStripMenuItem wanderItem = new ToolStripMenuItem("自动散步");
        private readonly ToolStripMenuItem topMostItem = new ToolStripMenuItem("始终置顶");
        private readonly ToolStripMenuItem autoWeatherItem = new ToolStripMenuItem("跟随所在地实时天气");
        private readonly ToolStripMenuItem automaticHolidayItem = new ToolStripMenuItem("按北京时间自动换装");
        private readonly ToolStripMenuItem noHolidayItem = new ToolStripMenuItem("关闭节日装扮");
        private readonly ToolStripMenuItem automaticSolarTermItem = new ToolStripMenuItem("节气当天自动换装");
        private readonly ToolStripMenuItem noSolarTermItem = new ToolStripMenuItem("关闭节气装扮");
        private readonly ToolStripMenuItem feedItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("开机启动小鲸");
        private readonly ToolStripMenuItem menuTitle = new ToolStripMenuItem();
        private readonly ToolStripMenuItem quitItem = new ToolStripMenuItem();
        private readonly Dictionary<string, ToolStripMenuItem> holidayItems = new Dictionary<string, ToolStripMenuItem>();
        private readonly Dictionary<string, ToolStripMenuItem> solarTermItems = new Dictionary<string, ToolStripMenuItem>();
        private readonly Dictionary<string, ToolStripMenuItem> weatherItems = new Dictionary<string, ToolStripMenuItem>();
        private readonly Dictionary<int, ToolStripMenuItem> sizeItems = new Dictionary<int, ToolStripMenuItem>();
        private NotifyIcon trayIcon;
        private Icon trayGraphic;
        private WeatherInfo weather;
        private QuotaInfo quota;
        private HardwareInfo hardware;
        private bool telemetryBusy;
        private bool weatherBusy;
        private bool quotaBusy;
        private bool closed;
        private bool ready;
        private bool diagnosticPreview;
        private bool leftPressed;
        private bool dragging;
        private bool? activityBusy;
        private bool? activityDesktopOpen;
        private string activitySource = "尚未连接 Codex 工作状态";
        private bool hasSavedLocation;
        private bool wanderEnabled;
        private int displayMode = 1;
        private int sizePercent = 100;
        private int fishCount;
        private int walkDirection = 1;
        private int previewSeverity = 1;
        private string previewCondition;
        private string holidayMode = "auto";
        private string solarTermMode = "auto";
        private string activeHoliday;
        private string petName = "小鲸";
        private DateTime lastHolidayDate;
        private string assetWarning;
        private Point dragOffset;
        private Point pressCursor;
        private Point previousMouse;
        private long feedStarted = -10000;
        private long walkUntil;
        private long nextWanderAt = 30000;
        private double wanderRemaining;
        private double wanderPositionX;
        private long previousTick;
        private DateTime lastSavedAt;

        public bool HasRenderedLayeredFrame { get; private set; }
        public string LastRenderError { get; private set; }
        public event EventHandler ActivityRefreshRequested;
        public string PetName { get { return petName; } }

        public PetForm(AppServices services, string applicationDirectory)
        {
            if (services == null) throw new ArgumentNullException("services");
            if (String.IsNullOrWhiteSpace(applicationDirectory)) throw new ArgumentNullException("applicationDirectory");
            this.services = services;
            this.applicationDirectory = Path.GetFullPath(applicationDirectory);
            settingsPath = Path.Combine(this.applicationDirectory, "settings.ini");
            Text = "小鲸 · 诸暨桌面宠物";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            SetStyle(ControlStyles.StandardDoubleClick, true);
            LoadAssets();
            LoadSettings();
            UpdateIdentity();
            RefreshHolidayDate(true);
            ClientSize = ScaledSize();
            BuildMenu();
            BuildTrayIcon();
            if (!hasSavedLocation)
            {
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(workArea.Right - Width - 28, workArea.Bottom - Height - 22);
            }
            ClampToScreen();
            InitializeHomePosition();
            animationTimer.Interval = 33;
            animationTimer.Tick += delegate { AnimationTick(); };
            telemetryTimer.Interval = 5000;
            telemetryTimer.Tick += async delegate { await RefreshHardwareAsync(); };
            weatherTimer.Interval = 600000;
            weatherTimer.Tick += async delegate { await RefreshWeatherAsync(); };
            quotaTimer.Interval = 120000;
            quotaTimer.Tick += async delegate { await RefreshQuotaAsync(); };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
            InitializePowerEvents();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (!interactiveCheck) cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                if (!interactiveCheck) cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                else cp.ExStyle = (cp.ExStyle & ~0x00080080) | 0x00040000;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ready = true;
            RenderWindow();
            if (BackgroundPollingSuspended) return;
            animationTimer.Start();
            if (diagnosticPreview) return;
            telemetryTimer.Start();
            weatherTimer.Start();
            quotaTimer.Start();
            await RefreshAllAsync();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (interactiveCheck)
            {
                e.Graphics.Clear(Color.FromArgb(238, 244, 252));
                using (Bitmap bitmap = RenderFrame(animationClock.ElapsedMilliseconds)) e.Graphics.DrawImageUnscaled(bitmap, 0, 0);
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            RecordInputEvent("down", e.Location);
            if (TryLifeClick(e.Location, animationClock.ElapsedMilliseconds)) return;
            Point cursor = Cursor.Position;
            dragOffset = new Point(cursor.X - Left, cursor.Y - Top);
            pressCursor = cursor;
            previousMouse = cursor;
            leftPressed = true;
            dragging = false;
            InteractionPress(e.Location, animationClock.ElapsedMilliseconds);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!leftPressed) return;
            RecordInputEvent("move", e.Location);
            Point cursor = Cursor.Position;
            if (!dragging)
            {
                Size threshold = SystemInformation.DragSize;
                if (Math.Abs(cursor.X - pressCursor.X) <= threshold.Width / 2 && Math.Abs(cursor.Y - pressCursor.Y) <= threshold.Height / 2) return;
                dragging = true;
                BeginLift(animationClock.ElapsedMilliseconds);
                wanderRemaining = 0;
                Cursor = Cursors.SizeAll;
            }
            int delta = cursor.X - previousMouse.X;
            if (delta != 0) walkDirection = delta < 0 ? -1 : 1;
            ReportDragMotion(delta, cursor.Y - previousMouse.Y, animationClock.ElapsedMilliseconds);
            previousMouse = cursor;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X - dragOffset.X;
            int y = cursor.Y - dragOffset.Y;
            Location = new Point(Clamp(x, area.Left, area.Right - Width), Clamp(y, area.Top, area.Bottom - Height));
            walkUntil = animationClock.ElapsedMilliseconds + 500;
            // Position follows each mouse event; raster drawing is coalesced by the fast animation timer.
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                RecordInputEvent("up", e.Location);
                bool didDrag = dragging;
                InteractionRelease(e.Location, animationClock.ElapsedMilliseconds, didDrag, false);
                leftPressed = false;
                dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                if (didDrag) walkUntil = animationClock.ElapsedMilliseconds + 350;
                nextWanderAt = animationClock.ElapsedMilliseconds + 25000;
                if (didDrag) SaveSettings();
            }
            else if (e.Button == MouseButtons.Right)
            {
                UpdateMenuChecks();
                petMenu.Show(this, e.Location);
            }
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                if (leftPressed) InteractionRelease(Point.Empty, animationClock.ElapsedMilliseconds, dragging, true);
                leftPressed = false;
                dragging = false;
                Cursor = Cursors.Default;
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                if (animationClock.ElapsedMilliseconds < suppressClickUntil) return;
                pendingPatAt = -1; suppressClickUntil = animationClock.ElapsedMilliseconds + SystemInformation.DoubleClickTime;
                FeedFish();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SuspendBackgroundPolling(e.CloseReason == CloseReason.WindowsShutDown);
            RemovePowerEvents();
            closed = true;
            animationTimer.Stop();
            telemetryTimer.Stop();
            weatherTimer.Stop();
            quotaTimer.Stop();
            SaveSettings();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
            if (trayIcon != null) trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                services.PauseBackgroundQueries(false);
                RemovePowerEvents();
                resumeRefreshTimer.Dispose();
                ReleaseRenderCaches();
                closed = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
                animationTimer.Dispose();
                telemetryTimer.Dispose();
                weatherTimer.Dispose();
                quotaTimer.Dispose();
                petMenu.Dispose();
                if (trayIcon != null) trayIcon.Dispose();
                if (trayGraphic != null) trayGraphic.Dispose();
                foreach (Image img in images.Values) img.Dispose();
                images.Clear();
            }
            base.Dispose(disposing);
        }

        // A deterministic preview hook lets the portable package render a PNG
        // without installing or launching any persistent background process.
        public void SavePreview(string path)
        {
            using (Bitmap bitmap = RenderFrame(1200)) bitmap.Save(path, ImageFormat.Png);
        }

        public void SetPreviewWeather(string condition, int severity)
        {
            previewCondition = condition;
            previewSeverity = Clamp(severity, 1, 3);
            UpdateMenuChecks();
            RenderWindow();
        }

        public void SetPreviewData(WeatherInfo weatherData, QuotaInfo quotaData, HardwareInfo hardwareData)
        {
            weather = weatherData;
            quota = quotaData;
            hardware = hardwareData;
            RenderWindow();
        }

        public void SetPreviewMode(int mode)
        {
            if (mode != 1 && mode != 2) throw new ArgumentOutOfRangeException("mode", "Display mode must be 1 or 2.");
            displayMode = mode;
            UpdateMenuChecks();
            RenderWindow();
        }

        public void DisableAutomaticPollingForPreview()
        {
            diagnosticPreview = true;
            telemetryTimer.Stop();
            weatherTimer.Stop();
            quotaTimer.Stop();
        }

        public void SetActivityState(bool? busy, string source)
        {
            SetActivityState(busy, true, source);
        }

        public void SetActivityState(bool? busy, bool? desktopOpen, string source)
        {
            if (closed) return;
            if (IsHandleCreated && InvokeRequired)
            {
                try { BeginInvoke((Action)delegate { SetActivityState(busy, desktopOpen, source); }); }
                catch (InvalidOperationException) { }
                return;
            }
            bool wasWorking = lastConfirmedWorking == true;
            activityBusy = desktopOpen == false ? false : busy;
            if (activityBusy.HasValue) lastConfirmedWorking = activityBusy;
            if (wasWorking && activityBusy == false && desktopOpen == true) finishQueuedAt = animationClock.ElapsedMilliseconds;
            if (activityBusy == true) { finishQueuedAt = -1; stretchStarted = -10000; }
            ObserveWorkingTransition(wasWorking, animationClock.ElapsedMilliseconds);
            activityDesktopOpen = desktopOpen;
            activitySource = String.IsNullOrWhiteSpace(source) ? "状态来源未提供" : ShortError(source);
            if (activityBusy == true) { wanderRemaining = 0; nextWanderAt = animationClock.ElapsedMilliseconds + 25000; }
            RenderWindow();
        }

        public string ActivityLabel
        {
            get { return new CodexActivityInfo { IsWorking = activityBusy, IsDesktopOpen = activityDesktopOpen }.StatusLabel; }
        }

        public void SetPreviewActivity(bool? busy)
        {
            SetActivityState(busy, "预览演示数据，非真实工作状态");
        }

        public void SetPetName(string name)
        {
            string normalized;
            if (!PetNameDialog.TryNormalize(name, out normalized)) throw new ArgumentException("名称须为 1–20 个字符。", "name");
            petName = normalized;
            UpdateIdentity();
            SaveSettings();
            RenderWindow();
        }

        private void UpdateIdentity()
        {
            string place = services.CurrentWeatherLocation.Name;
            Text = petName + " · " + place + "桌面宠物";
            menuTitle.Text = petName + " · " + place;
            quitItem.Text = "退出" + petName;
            if (trayIcon != null) trayIcon.Text = SafeTrayText(petName + " · 双击投喂，右键切换显示");
        }

        private static string SafeTrayText(string text)
        {
            if (text.Length <= 63) return text;
            int length = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                int end = elements.ElementIndex + elements.GetTextElement().Length;
                if (end > 63) break;
                length = end;
            }
            if (length == 0)
            {
                length = 63;
                if (Char.IsHighSurrogate(text[length - 1])) length--;
            }
            return text.Substring(0, length);
        }

        public void ApplyWeatherLocation(WeatherLocationData location)
        {
            services.SetWeatherLocation(location);
            weather = null;
            previewCondition = null;
            UpdateIdentity();
            UpdateMenuChecks();
            SaveSettings();
            RenderWindow();
        }

        public void SetPreviewHoliday(string id)
        {
            if (id != "auto" && id != "off" && Array.IndexOf(HolidayIds, id) < 0) throw new ArgumentOutOfRangeException("id");
            holidayMode = id;
            if (SolarTermCalendar.IsValidId(solarTermMode)) solarTermMode = "auto";
            RefreshHolidayDate(true);
            UpdateHolidayLayout();
            UpdateMenuChecks();
            RenderWindow();
        }

        public void SetPreviewSolarTerm(string id)
        {
            if (id != "auto" && id != "off" && !SolarTermCalendar.IsValidId(id)) throw new ArgumentOutOfRangeException("id");
            solarTermMode = id;
            if (SolarTermCalendar.IsValidId(id)) holidayMode = "auto";
            RefreshHolidayDate(true);
            UpdateHolidayLayout();
            UpdateMenuChecks();
            RenderWindow();
        }

        public void SaveAnimationPreview(string path, long elapsed, bool feeding, bool christmasInteraction)
        {
            long oldFeed = feedStarted;
            try
            {
                feedStarted = feeding ? 0 : -10000;
                using (Bitmap bitmap = RenderFrame(elapsed)) bitmap.Save(path, ImageFormat.Png);
            }
            finally { feedStarted = oldFeed; }
        }

        public async Task RefreshAllAsync()
        {
            if (BackgroundPollingSuspended) return;
            RequestActivityRefresh();
            await Task.WhenAll(RefreshHardwareAsync(), RefreshWeatherAsync(), RefreshQuotaAsync());
        }

        private void RequestActivityRefresh()
        {
            EventHandler handler = ActivityRefreshRequested;
            if (!BackgroundPollingSuspended && handler != null) handler(this, EventArgs.Empty);
        }

        private async Task RefreshHardwareAsync()
        {
            if (BackgroundPollingSuspended || telemetryBusy) return;
            long epoch = pollingEpoch;
            telemetryBusy = true;
            try
            {
                HardwareInfo result = await services.ReadHardwareAsync();
                if (AcceptQueryResult(epoch) && result != null) hardware = result;
            }
            catch (Exception ex)
            {
                if (AcceptQueryResult(epoch)) hardware = new HardwareInfo { Error = ShortError(ex.Message), UpdatedAt = DateTime.Now };
            }
            finally
            {
                telemetryBusy = false;
                if (!closed) RenderWindow();
            }
        }

        private async Task RefreshWeatherAsync()
        {
            if (BackgroundPollingSuspended || weatherBusy) return;
            long epoch = pollingEpoch;
            do
            {
                long locationVersion = services.WeatherLocationVersion;
                weatherBusy = true;
                try
                {
                    WeatherInfo result = await services.ReadWeatherAsync();
                    if (AcceptQueryResult(epoch) && result != null && result.LocationVersion == services.WeatherLocationVersion) weather = result;
                }
                catch (Exception ex)
                {
                    if (AcceptQueryResult(epoch) && locationVersion == services.WeatherLocationVersion)
                        weather = new WeatherInfo { Condition = "sun", Severity = 1, Error = ShortError(ex.Message), UpdatedAt = DateTime.UtcNow, LocationVersion = locationVersion };
                }
                finally
                {
                    weatherBusy = false;
                    if (!closed) RenderWindow();
                }
                if (!AcceptQueryResult(epoch) || locationVersion == services.WeatherLocationVersion) break;
                // A location change during an older request gets a fresh read now.
            } while (AcceptQueryResult(epoch));
        }

        private async Task RefreshQuotaAsync()
        {
            if (BackgroundPollingSuspended || quotaBusy) return;
            long epoch = pollingEpoch;
            quotaBusy = true;
            try
            {
                QuotaInfo result = await services.ReadQuotaAsync();
                if (AcceptQueryResult(epoch) && result != null) quota = result;
            }
            catch (Exception ex)
            {
                if (AcceptQueryResult(epoch)) quota = new QuotaInfo { Error = ShortError(ex.Message), UpdatedAt = DateTime.Now };
            }
            finally
            {
                quotaBusy = false;
                if (!closed) RenderWindow();
            }
        }

        private void BuildMenu()
        {
            petMenu.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            petMenu.ShowImageMargin = false;
            petMenu.ShowCheckMargin = true;
            petMenu.ShowItemToolTips = true;
            petMenu.Opening += delegate { UpdateMenuChecks(); };
            menuTitle.Enabled = false;
            petMenu.Items.Add(menuTitle);
            petMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem rename = new ToolStripMenuItem("修改宠物名称…");
            rename.Click += delegate
            {
                using (PetNameDialog dialog = new PetNameDialog(petName))
                    if (dialog.ShowDialog(this) == DialogResult.OK) SetPetName(dialog.SelectedName);
            };
            petMenu.Items.Add(rename);
            ToolStripMenuItem location = new ToolStripMenuItem("设置天气所在地…");
            location.Click += async delegate
            {
                using (WeatherLocationDialog dialog = new WeatherLocationDialog(services.CurrentWeatherLocation))
                {
                    EventHandler pauseSearch = delegate { dialog.SetBackgroundQueriesPaused(BackgroundPollingSuspended); };
                    BackgroundPollingChanged += pauseSearch;
                    try {
                        pauseSearch(this, EventArgs.Empty);
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        ApplyWeatherLocation(dialog.SelectedLocation);
                        await RefreshWeatherAsync();
                    } finally { BackgroundPollingChanged -= pauseSearch; }
                }
            };
            petMenu.Items.Add(location);
            petMenu.Items.Add(new ToolStripSeparator());
            quotaModeItem.Click += delegate { SetDisplayMode(1); };
            telemetryModeItem.Click += delegate { SetDisplayMode(2); };
            petMenu.Items.Add(quotaModeItem);
            petMenu.Items.Add(telemetryModeItem);
            petMenu.Items.Add(new ToolStripSeparator());
            feedItem.Click += delegate { FeedFish(); };
            petMenu.Items.Add(feedItem);
            BuildInteractionMenu();
            wanderItem.Click += delegate
            {
                wanderEnabled = !wanderEnabled;
                wanderRemaining = 0;
                nextWanderAt = animationClock.ElapsedMilliseconds + 2000;
                SaveSettings();
                UpdateMenuChecks();
            };
            petMenu.Items.Add(wanderItem);
            topMostItem.Click += delegate { TopMost = !TopMost; SaveSettings(); UpdateMenuChecks(); };
            petMenu.Items.Add(topMostItem);
            startupItem.Click += delegate
            {
                try { StartupManager.SetEnabled(!StartupManager.IsEnabled(Application.ExecutablePath), Application.ExecutablePath); UpdateMenuChecks(); }
                catch (Exception) { MessageBox.Show(this, "开机启动设置未能保存。可在数据来源与状态中查看当前设置。", petName + " · 开机启动", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            petMenu.Items.Add(startupItem);
            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("宠物大小");
            AddSizeMenu(sizeMenu, 80, "小  ·  80%");
            AddSizeMenu(sizeMenu, 100, "标准  ·  100%");
            AddSizeMenu(sizeMenu, 125, "大  ·  125%");
            petMenu.Items.Add(sizeMenu);
            ToolStripMenuItem previewMenu = new ToolStripMenuItem("天气外观预览");
            autoWeatherItem.Click += delegate { previewCondition = null; UpdateMenuChecks(); RenderWindow(); };
            previewMenu.DropDownItems.Add(autoWeatherItem);
            previewMenu.DropDownItems.Add(new ToolStripSeparator());
            AddWeatherMenu(previewMenu, "sun", "晴天", new string[] { "晴朗", "强日照", "酷热" });
            AddWeatherMenu(previewMenu, "wind", "风", new string[] { "微风", "大风", "强风" });
            AddWeatherMenu(previewMenu, "rain", "雨", new string[] { "小雨", "中雨", "暴雨" });
            AddWeatherMenu(previewMenu, "snow", "雪", new string[] { "小雪", "中雪", "大雪" });
            petMenu.Items.Add(previewMenu);
            ToolStripMenuItem holidayMenu = new ToolStripMenuItem("节日装扮");
            automaticHolidayItem.Click += delegate { SetPreviewHoliday("auto"); SaveSettings(); };
            noHolidayItem.Click += delegate { SetPreviewHoliday("off"); SaveSettings(); };
            holidayMenu.DropDownItems.Add(automaticHolidayItem);
            holidayMenu.DropDownItems.Add(noHolidayItem);
            holidayMenu.DropDownItems.Add(new ToolStripSeparator());
            foreach (string holidayId in HolidayIds)
            {
                string capturedId = holidayId;
                ToolStripMenuItem holidayItem = new ToolStripMenuItem(HolidayCalendar.GetName(capturedId));
                holidayItem.Click += delegate { SetPreviewHoliday(capturedId); SaveSettings(); };
                holidayItems.Add(capturedId, holidayItem);
                holidayMenu.DropDownItems.Add(holidayItem);
            }
            petMenu.Items.Add(holidayMenu);
            ToolStripMenuItem termsMenu = new ToolStripMenuItem("二十四节气装扮");
            automaticSolarTermItem.Click += delegate { SetPreviewSolarTerm("auto"); SaveSettings(); };
            noSolarTermItem.Click += delegate { SetPreviewSolarTerm("off"); SaveSettings(); };
            termsMenu.DropDownItems.Add(automaticSolarTermItem);
            termsMenu.DropDownItems.Add(noSolarTermItem);
            termsMenu.DropDownItems.Add(new ToolStripSeparator());
            string[] termIds = SolarTermCalendar.Ids;
            string[] seasons = { "春季", "夏季", "秋季", "冬季" };
            for (int season = 0; season < 4; season++) {
                ToolStripMenuItem group = new ToolStripMenuItem(seasons[season]);
                for (int termIndex = season * 6; termIndex < season * 6 + 6; termIndex++) {
                    string capturedTerm = termIds[termIndex];
                    ToolStripMenuItem item = new ToolStripMenuItem(SolarTermCalendar.GetName(capturedTerm) + " · " + SolarTermCalendar.GetFoodName(capturedTerm));
                    item.Click += delegate { SetPreviewSolarTerm(capturedTerm); SaveSettings(); };
                    solarTermItems.Add(capturedTerm, item); group.DropDownItems.Add(item);
                }
                termsMenu.DropDownItems.Add(group);
            }
            petMenu.Items.Add(termsMenu);
            petMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem refresh = new ToolStripMenuItem("立即刷新全部状态");
            refresh.Click += async delegate { await RefreshAllAsync(); };
            petMenu.Items.Add(refresh);
            ToolStripMenuItem refreshActivity = new ToolStripMenuItem("立即刷新工作状态");
            refreshActivity.Click += delegate { RequestActivityRefresh(); };
            petMenu.Items.Add(refreshActivity);
            ToolStripMenuItem reset = new ToolStripMenuItem("移回屏幕右下角");
            reset.Click += delegate
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Right - Width - 28, area.Bottom - Height - 22);
                ClampToScreen();
                SaveSettings();
                Show();
                RenderWindow();
            };
            petMenu.Items.Add(reset);
            ToolStripMenuItem diagnostics = new ToolStripMenuItem("查看数据来源与状态");
            diagnostics.Click += delegate { ShowDiagnostics(); };
            petMenu.Items.Add(diagnostics);
            petMenu.Items.Add(new ToolStripSeparator());
            quitItem.Click += delegate { Close(); };
            petMenu.Items.Add(quitItem);
            UpdateMenuChecks();
        }

        private void AddSizeMenu(ToolStripMenuItem parent, int percent, string label)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Click += delegate { ChangeSize(percent); };
            sizeItems.Add(percent, item);
            parent.DropDownItems.Add(item);
        }

        private void AddWeatherMenu(ToolStripMenuItem parent, string condition, string label, string[] levels)
        {
            ToolStripMenuItem group = new ToolStripMenuItem(label);
            for (int i = 0; i < 3; i++)
            {
                int severity = i + 1;
                ToolStripMenuItem item = new ToolStripMenuItem(severity.ToString(CultureInfo.InvariantCulture) + "  " + levels[i]);
                item.Click += delegate { SetPreviewWeather(condition, severity); };
                weatherItems.Add(condition + "-" + severity.ToString(CultureInfo.InvariantCulture), item);
                group.DropDownItems.Add(item);
            }
            parent.DropDownItems.Add(group);
        }

        private void UpdateMenuChecks()
        {
            UpdateIdentity();
            quotaModeItem.Checked = displayMode == 1;
            telemetryModeItem.Checked = displayMode == 2;
            wanderItem.Checked = wanderEnabled;
            topMostItem.Checked = TopMost;
            autoWeatherItem.Checked = previewCondition == null;
            automaticHolidayItem.Checked = holidayMode == "auto";
            noHolidayItem.Checked = holidayMode == "off";
            automaticSolarTermItem.Checked = solarTermMode == "auto";
            noSolarTermItem.Checked = solarTermMode == "off";
            feedItem.Text = "喂" + CurrentFoodName() + " · 双击也可以";
            try { SetStartupMenuState(StartupManager.IsEnabled(Application.ExecutablePath)); }
            catch (Exception) { SetStartupMenuState(null); }
            UpdateLifeMenuChecks();
            foreach (KeyValuePair<string, ToolStripMenuItem> holidayItem in holidayItems) holidayItem.Value.Checked = holidayMode == holidayItem.Key;
            foreach (KeyValuePair<string, ToolStripMenuItem> termItem in solarTermItems) termItem.Value.Checked = solarTermMode == termItem.Key;
            foreach (KeyValuePair<int, ToolStripMenuItem> item in sizeItems) item.Value.Checked = item.Key == sizePercent;
            foreach (KeyValuePair<string, ToolStripMenuItem> item in weatherItems)
                item.Value.Checked = previewCondition != null && item.Key == previewCondition + "-" + previewSeverity.ToString(CultureInfo.InvariantCulture);
        }

        private void BuildTrayIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush body = new SolidBrush(Color.FromArgb(84, 128, 184)))
                    {
                        g.FillEllipse(body, 2, 7, 24, 19);
                        g.FillPolygon(body, new Point[] { new Point(23, 18), new Point(30, 8), new Point(30, 20) });
                    }
                    using (Brush belly = new SolidBrush(Color.FromArgb(229, 243, 250))) g.FillEllipse(belly, 6, 17, 16, 8);
                    using (Brush eye = new SolidBrush(Color.FromArgb(31, 42, 68))) g.FillEllipse(eye, 7, 12, 3, 4);
                    using (Pen water = new Pen(Color.FromArgb(94, 194, 226), 2F))
                    {
                        g.DrawLine(water, 14, 7, 14, 3);
                        g.DrawArc(water, 9, 1, 5, 6, 170, 120);
                        g.DrawArc(water, 14, 1, 5, 6, 240, 110);
                    }
                }
                IntPtr iconHandle = bitmap.GetHicon();
                try { using (Icon borrowed = Icon.FromHandle(iconHandle)) trayGraphic = (Icon)borrowed.Clone(); }
                finally { NativeMethods.DestroyIcon(iconHandle); }
            }
            trayIcon = new NotifyIcon();
            trayIcon.Icon = trayGraphic;
            trayIcon.Text = SafeTrayText(petName + " · 双击投喂，右键切换显示");
            trayIcon.ContextMenuStrip = petMenu;
            trayIcon.DoubleClick += delegate { Show(); ClampToScreen(); RenderWindow(); };
            trayIcon.Visible = true;
        }

        private void FeedFish()
        {
            if (lifted || falling) return;
            PauseHomeWalkForInteraction();
            mood = Clamp(mood + 3, 0, 100); fullness = Clamp(fullness + 10, 0, 100); StopLeisure(animationClock.ElapsedMilliseconds);
            pendingPatAt = -1; petStarted = -10000; stretchStarted = -10000; landStarted = -10000; closeLaptopStarted = -10000;
            feedStarted = animationClock.ElapsedMilliseconds;
            if (fishCount < Int32.MaxValue) fishCount++;
            wanderRemaining = 0;
            nextWanderAt = feedStarted + 20000;
            SaveSettings();
            RenderWindow();
        }

        private void SetDisplayMode(int mode)
        {
            displayMode = mode;
            UpdateMenuChecks();
            SaveSettings();
            RenderWindow();
        }

        private void RefreshHolidayDate(bool force)
        {
            DateTime date = ShanghaiTime(DateTime.UtcNow).Date;
            if (!force && date == lastHolidayDate) return;
            lastHolidayDate = date;
            string previous = activeHoliday;
            activeHoliday = SolarTermCalendar.ResolveOutfit(date, holidayMode, solarTermMode);
            if (previous != activeHoliday)
            {
                if (ready) { UpdateHolidayLayout(); UpdateMenuChecks(); }
            }
        }

        private void UpdateHolidayLayout()
        {
            Size dimensions = ScaledSize();
            if (ClientSize != dimensions) ClientSize = dimensions;
            ClampToScreen();
        }

        private Size ScaledSize()
        {
            int width = activeHoliday == "christmas" ? ChristmasWidth : BaseWidth;
            return new Size((int)Math.Round(width * sizePercent / 100.0), (int)Math.Round(BaseHeight * sizePercent / 100.0));
        }

        private void ChangeSize(int percent)
        {
            int anchorX = Left + Width / 2;
            int anchorY = Bottom;
            sizePercent = percent;
            ClientSize = ScaledSize();
            Location = new Point(anchorX - Width / 2, anchorY - Height);
            ClampToScreen();
            UpdateMenuChecks();
            SaveSettings();
            RenderWindow();
        }

        private void AnimationTick()
        {
            if (closed || !ready) return;
            long now = animationClock.ElapsedMilliseconds;
            RefreshHolidayDate(false);
            InteractionTick(now);
            UpdateMotionCadence(now);
            double elapsed = Math.Min(0.1, Math.Max(0, (now - previousTick) / 1000.0));
            previousTick = now;
            ApplyFallPhysics(now, elapsed); UpdateHomeFlight(now); UpdateHomeWalk(now, elapsed); ObservePointer(now);
            if (wanderEnabled && activityBusy != true && !leftPressed && String.IsNullOrEmpty(leisure) && !InteractionBlocksWork(now) && !FeedingAt(now) && !petMenu.Visible)
            {
                if (wanderRemaining <= 0 && now >= nextWanderAt)
                {
                    walkDirection = random.Next(2) == 0 ? -1 : 1;
                    wanderRemaining = 45 + random.Next(70);
                    wanderPositionX = Left;
                }
                if (wanderRemaining > 0)
                {
                    Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                    double step = Math.Min(wanderRemaining, elapsed * 22 * sizePercent / 100.0);
                    wanderPositionX += step * walkDirection;
                    int x = (int)Math.Round(wanderPositionX);
                    if (x <= area.Left || x >= area.Right - Width)
                    {
                        x = Clamp(x, area.Left, area.Right - Width);
                        wanderRemaining = 0;
                    }
                    else wanderRemaining -= step;
                    Location = new Point(x, Top);
                    walkUntil = now + 150;
                    if (wanderRemaining <= 0)
                    {
                        nextWanderAt = now + 20000 + random.Next(15000);
                        SaveSettings();
                    }
                }
            }
            RenderWindow();
        }

        private string CurrentCondition()
        {
            string condition = previewCondition ?? (weather == null ? "sun" : weather.Condition);
            if (condition != "sun" && condition != "wind" && condition != "rain" && condition != "snow") return "sun";
            return condition;
        }

        private int CurrentSeverity()
        {
            return Clamp(previewCondition == null ? (weather == null ? 1 : weather.Severity) : previewSeverity, 1, 3);
        }

        private Image CurrentPetImage(long now)
        {
            Image selected;
            Image interactionImage = InteractionImage(now);
            if (interactionImage != null) return interactionImage;
            if (now < lookUntil && String.IsNullOrEmpty(leisure) && images.TryGetValue("loops/" + CurrentLoopKey() + "/frame-00", out selected)) return selected;
            // The idle hot-weather sheet has a fan baked near the cheek. Working
            // uses clean artwork, so covering the body with a laptop cannot leave
            // fan blades or a third hand visible beside the face.
            if (activeHoliday == null && IsWorkVisual(now) && CurrentCondition() == "sun" && CurrentSeverity() == 3)
            {
                if (images.TryGetValue("loops/sun-3-work/frame-" + LoopFrameIndex(now).ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
                if (images.TryGetValue("weather/sun-3-work", out selected)) return selected;
                if (images.TryGetValue("base", out selected)) return selected;
            }
            bool ordinaryManualAction = activeHoliday == null && activityBusy != true && (CurrentLoopKey() == "base" || CurrentCondition() == "sun" && CurrentSeverity() == 1);
            if (ordinaryManualAction && now - feedStarted >= 0 && now - feedStarted < FeedingDuration)
            {
                int feedingFrame = Math.Min(5, (int)((now - feedStarted) / 360));
                if (images.TryGetValue("feed/frame-" + feedingFrame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            }
            if (ordinaryManualAction && (dragging || now < walkUntil || wanderRemaining > 0))
            {
                int walkingFrame = (int)(Math.Max(0, now) / 110) % 8;
                string direction = walkDirection < 0 ? "walk-left" : "walk-right";
                if (images.TryGetValue(direction + "/frame-" + walkingFrame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            }
            string loopKey = CurrentLoopKey();
            if (HasLoop(loopKey))
            {
                int loopFrame = activeHoliday == null && CurrentCondition() == "wind"
                    ? (int)(Math.Max(0, now) / (CurrentSeverity() == 1 ? 420 : CurrentSeverity() == 2 ? 300 : 210)) % 4
                    : LoopFrameIndex(now);
                if (images.TryGetValue("loops/" + loopKey + "/frame-" + loopFrame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            }
            if (activeHoliday != null && images.TryGetValue("holidays/" + activeHoliday, out selected)) return selected;
            if (IsWorkVisual(now) || !ordinaryManualAction)
            {
                if (images.TryGetValue("weather/" + CurrentCondition() + "-" + CurrentSeverity(), out selected)) return selected;
            }
            if (now - feedStarted >= 0 && now - feedStarted < FeedingDuration)
            {
                int frame = Math.Min(5, (int)((now - feedStarted) / 360));
                if (images.TryGetValue("feed/frame-" + frame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            }
            if (dragging || now < walkUntil || wanderRemaining > 0)
            {
                int frame = (int)(now / 110) % 8;
                string direction = walkDirection < 0 ? "walk-left" : "walk-right";
                if (images.TryGetValue(direction + "/frame-" + frame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            }
            int idleFrame = (int)(now / 260) % 6;
            if (CurrentCondition() == "sun" && CurrentSeverity() == 1 && images.TryGetValue("idle/frame-" + idleFrame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            string weatherKey = "weather/" + CurrentCondition() + "-" + CurrentSeverity().ToString(CultureInfo.InvariantCulture);
            if (images.TryGetValue(weatherKey, out selected)) return selected;
            if (images.TryGetValue("idle/frame-" + idleFrame.ToString("00", CultureInfo.InvariantCulture), out selected)) return selected;
            if (images.TryGetValue("base", out selected)) return selected;
            return null;
        }

        private string CurrentLoopKey()
        {
            if (activeHoliday != null) return activeHoliday;
            if (previewCondition == null && weather == null) return "base";
            return CurrentCondition() + "-" + CurrentSeverity().ToString(CultureInfo.InvariantCulture);
        }

        private bool HasLoop(string key)
        {
            for (int frame = 0; frame < 4; frame++) if (!images.ContainsKey("loops/" + key + "/frame-" + frame.ToString("00", CultureInfo.InvariantCulture))) return false;
            return true;
        }

        private int LoopFrameIndex(long now)
        {
            long phase = Math.Max(0, now) % 1840;
            if (phase < 1000) return 0;
            if (phase < 1140) return 1;
            if (phase < 1590) return 2;
            return 3;
        }

        private bool IsWorkVisual(long now)
        {
            return activityBusy == true && !dragging && now >= walkUntil && !FeedingAt(now) && !InteractionBlocksWork(now);
        }

        private string CurrentFoodName()
        {
            if (SolarTermCalendar.IsTermOutfit(activeHoliday)) return SolarTermCalendar.GetFoodName(activeHoliday);
            switch (activeHoliday)
            {
                case "springfestival": return "饺子";
                case "lantern": return "汤圆";
                case "qingming": return "青团";
                case "dragonboat": return "粽子";
                case "qixi": return "巧果";
                case "midautumn": return "月饼";
                case "chongyang": return "重阳糕";
                case "laba": return "腊八粥";
                case "halloween": return "南瓜饼";
                case "christmas": return "姜饼人";
                default: return CurrentCondition() == "sun" && CurrentSeverity() == 3 ? "冰棒" : "小鱼";
            }
        }

        private float PetBob(long now)
        {
            if (dragging || now < walkUntil) return (float)Math.Sin(now / 75.0) * 2.5F;
            return (float)Math.Sin(now / 560.0) * (HasLoop(CurrentLoopKey()) ? 0.6F : 1.3F);
        }

        private void PetPose(long now, out float offsetX, out float offsetY, out float angle)
        {
            offsetX = 0;
            offsetY = PetBob(now);
            angle = 0;
            if (InteractionPose(now, ref offsetX, ref offsetY, ref angle)) return;
            if (now - feedStarted >= 0 && now - feedStarted < FeedingDuration) return;
            if (activeHoliday != null) return;
            int severity = CurrentSeverity();
            if (IsWorkVisual(now)) { angle = (float)Math.Sin(now / 350.0) * 0.18F; return; }
            if (CurrentCondition() == "wind")
            {
                angle = (float)Math.Sin(now / 420.0) * severity * 0.22F;
                offsetX += (float)Math.Sin(now / 410.0) * severity * 0.25F;
            }
            else if (CurrentCondition() == "rain")
            {
                angle = (float)Math.Sin(now / 430.0) * (0.5F + severity * 0.4F);
                offsetX += (float)Math.Sin(now / 430.0) * severity * 1.2F;
                offsetY += severity * 0.7F;
            }
            else if (CurrentCondition() == "snow")
            {
                angle = (float)Math.Sin(now / 75.0) * (0.25F + severity * 0.25F);
                offsetX += (float)Math.Sin(now / 65.0) * (0.4F + severity * 0.45F);
            }
            else angle = (float)Math.Sin(now / 620.0) * (0.2F + severity * 0.2F);
        }

        private Bitmap RenderFrame(long now)
        {
            Size dimensions = ScaledSize();
            Bitmap bitmap = new Bitmap(dimensions.Width, dimensions.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                float scale = sizePercent / 100F;
                g.ScaleTransform(scale, scale);
                DrawCachedInformationCard(g);
                DrawWeatherEffects(g, now, false);
                DrawLifeWorld(g, now);
                if (activeHoliday == "christmas") DrawChristmasGifts(g);
                Image pet = CurrentPetImage(now);
                float offsetX, offsetY, angle;
                PetPose(now, out offsetX, out offsetY, out angle);
                GraphicsState petState = g.Save();
                g.TranslateTransform(136 + offsetX, 226 + offsetY);
                g.RotateTransform(angle);
                g.TranslateTransform(-136, -226);

                if (pet != null && returnFlying) DrawReturnFlight(g, pet, now);
                else if (pet != null && FlightLandingAt(now)) DrawFlightLanding(g, now);
                else if (pet != null && (lifted || falling)) DrawSuspendedAction(g, pet, now);
                else if (pet != null && homeWalking) DrawHomeWalking(g, pet, now);
                else if (DrawAttention(g, now)) { }
                else if (DrawLeisureMotion(g, now)) { }
                else if (pet != null) g.DrawImage(pet, UsingActionArtwork(now) || activeHoliday == null && CurrentCondition() == "wind"
                    ? new RectangleF(8, 122, 256, 208)
                    : activeHoliday == null && IsWorkVisual(now) && CurrentCondition() == "sun" && CurrentSeverity() == 3 && pet.Width == 224
                        ? new RectangleF(24, 122, 224, 208) : new RectangleF(40, 122, 192, 208));
                else DrawMissingPet(g, 0);
                if (IsWorkVisual(now) || ClosingLaptopAt(now)) DrawLaptop(g, now);
                g.Restore(petState);
                DrawWeatherEffects(g, now, true);
                DrawInteractionEffects(g, now);
                if (FeedingAt(now)) DrawFeeding(g, now - feedStarted);
                string hint = now - feedStarted >= 0 && now - feedStarted < FeedingDuration
                    ? "好吃！谢谢你的" + (activeHoliday == null ? "小鱼" : CurrentFoodName()) + " ♡"
                    : (dragging || now < walkUntil ? "陪你散步中…" : IsWorkVisual(now) ? "陪你工作中 · 双击投喂 · 右键菜单" : "单击摸头 · 按住提起 · 双击投喂");
                hint = InteractionHint(now) ?? hint;
                using (Font font = UiFont(7.2F, FontStyle.Regular))
                using (Brush text = new SolidBrush(Color.FromArgb(244, 234, 241, 251)))
                using (Brush background = new SolidBrush(Color.FromArgb(182, 34, 44, 69)))
                using (StringFormat format = CenterFormat())
                {
                    RectangleF hintRect = activeHoliday == "christmas" ? new RectangleF(12, 331, 194, 17) : new RectangleF(24, 331, 224, 17);
                    using (GraphicsPath path = RoundRectangle(hintRect, 8)) g.FillPath(background, path);
                    g.DrawString(hint, font, text, hintRect, format);
                }
            }
            return bitmap;
        }

        private void DrawInformationCard(Graphics g)
        {
            RectangleF panel = new RectangleF(10, 7, 252, 109);
            using (GraphicsPath shadowPath = RoundRectangle(new RectangleF(12, 10, 250, 109), 16))
            using (Brush shadow = new SolidBrush(Color.FromArgb(55, 10, 20, 34))) g.FillPath(shadow, shadowPath);
            using (GraphicsPath path = RoundRectangle(panel, 16))
            using (Brush fill = new SolidBrush(Color.FromArgb(240, 30, 42, 65)))
            using (Pen border = new Pen(Color.FromArgb(128, 102, 146, 186), 1F))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            using (Font titleFont = UiFont(9.3F, FontStyle.Bold))
            using (Brush title = new SolidBrush(Color.FromArgb(240, 246, 251)))
            using (StringFormat format = LeftFormat())
                g.DrawString(petName + " · " + (activeHoliday == null ? services.CurrentWeatherLocation.Name : HolidayCalendar.GetName(activeHoliday)), titleFont, title, new RectangleF(22, 14, 159, 19), format);
            RectangleF badge = new RectangleF(187, 14, 62, 19);
            using (GraphicsPath path = RoundRectangle(badge, 8))
            using (Brush badgeFill = new SolidBrush(Color.FromArgb(72, 83, 156, 193))) g.FillPath(badgeFill, path);
            using (Font badgeFont = UiFont(7.8F, FontStyle.Regular))
            using (Brush badgeText = new SolidBrush(Color.FromArgb(190, 229, 248)))
            using (StringFormat format = CenterFormat()) g.DrawString(displayMode == 1 ? "额度" : "硬件", badgeFont, badgeText, badge, format);
            using (Font weatherFont = UiFont(7.8F, FontStyle.Regular))
            using (Brush weatherText = new SolidBrush(Color.FromArgb(161, 192, 215)))
            using (StringFormat format = LeftFormat()) g.DrawString(WeatherCaption(), weatherFont, weatherText, new RectangleF(23, 36, 159, 16), format);
            RectangleF activityBadge = new RectangleF(189, 36, 60, 16);
            Color activityColor = activityBusy == true ? Color.FromArgb(143, 227, 216) : activityBusy == false ? Color.FromArgb(184, 198, 217) : Color.FromArgb(224, 193, 133);
            using (Font activityFont = UiFont(6.8F, FontStyle.Regular))
            using (Brush activityText = new SolidBrush(activityColor))
            using (StringFormat format = CenterFormat()) g.DrawString(ActivityLabel, activityFont, activityText, activityBadge, format);
            using (Pen divider = new Pen(Color.FromArgb(70, 124, 157, 188), 1F)) g.DrawLine(divider, 23, 55, 249, 55);
            if (displayMode == 1) DrawQuota(g);
            else DrawHardware(g);
        }

        private string WeatherCaption()
        {
            if (previewCondition != null && activeHoliday == null) return "外观预览：" + WeatherName(previewCondition, previewSeverity);
            if (weather == null) return "正在读取" + services.CurrentWeatherLocation.Name + "天气…";
            if (!String.IsNullOrWhiteSpace(weather.Error) && !weather.ObservedAt.HasValue && !weather.Temperature.HasValue)
                return "天气待联网 · 可右键预览";
            bool hasSummary = !String.IsNullOrWhiteSpace(weather.Summary);
            string caption = hasSummary ? weather.Summary : WeatherName(weather.Condition, weather.Severity);
            if (!hasSummary && weather.Temperature.HasValue) caption += "  " + weather.Temperature.Value.ToString("0.#", CultureInfo.InvariantCulture) + "°C";
            if (weather.IsStale) caption += " · 缓存";
            return caption;
        }

        private void DrawQuota(Graphics g)
        {
            List<QuotaWindow> available = quota == null ? null : quota.Windows;
            if (available == null || available.Count == 0)
            {
                DrawMainMessage(g, quota == null ? "正在读取账户剩余额度…" : "账户额度暂不可用", 66);
                DrawSecondaryMessage(g, quota == null ? "从当前 Codex 登录账户读取" : "右键 → 查看数据来源与状态", 89);
                return;
            }
            int count = Math.Min(2, available.Count);
            for (int i = 0; i < count; i++)
            {
                QuotaWindow window = available[i];
                float y = 62 + i * 21;
                string label = CompactQuotaLabel(window, i);
                using (Font font = UiFont(8.2F, FontStyle.Regular))
                using (Brush text = new SolidBrush(Color.FromArgb(223, 234, 245)))
                using (StringFormat format = LeftFormat()) g.DrawString(label, font, text, new RectangleF(23, y, 83, 16), format);
                bool known = window.RemainingPercent.HasValue && !Double.IsNaN(window.RemainingPercent.Value) && !Double.IsInfinity(window.RemainingPercent.Value);
                float fraction = known ? (float)(Math.Max(0, Math.Min(100, window.RemainingPercent.Value)) / 100) : 0;
                Color barColor = fraction <= 0.15F ? Color.FromArgb(246, 143, 151) : Color.FromArgb(106, 209, 220);
                using (GraphicsPath track = RoundRectangle(new RectangleF(109, y + 6, 63, 5), 2.5F))
                using (Brush fill = new SolidBrush(Color.FromArgb(88, 114, 137, 162))) g.FillPath(fill, track);
                if (fraction > 0)
                {
                    using (GraphicsPath progress = RoundRectangle(new RectangleF(109, y + 6, Math.Max(2, 63 * fraction), 5), 2.5F))
                    using (Brush fill = new SolidBrush(barColor)) g.FillPath(fill, progress);
                }
                string value = known ? "剩 " + Math.Max(0, Math.Min(100, window.RemainingPercent.Value)).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "未知";
                DrawFittedValue(g, value, new RectangleF(175, y - 1, 74, 19), known ? barColor : Color.FromArgb(166, 183, 201));
            }
            string footer = quota.IsStale
                ? (quota.UpdatedAt == default(DateTime) ? "缓存额度 · 等待刷新" : "缓存 " + ShanghaiTime(quota.UpdatedAt).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + " · 等待刷新")
                : "";
            if (!quota.IsStale)
            {
                QuotaWindow first = available[0];
                if (first.ResetAt.HasValue) footer = "主额度重置 " + ShanghaiTime(first.ResetAt.Value).ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
                else footer = "已更新 " + ShanghaiTime(quota.UpdatedAt).ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            DrawSecondaryMessage(g, footer, 101);
        }

        private void DrawHardware(Graphics g)
        {
            if (hardware == null)
            {
                DrawMainMessage(g, "正在读取硬件状态…", 66);
                DrawSecondaryMessage(g, "CPU · GPU 温度 / 物理内存", 89);
                return;
            }
            string cpu = hardware.CpuTemperature.HasValue ? hardware.CpuTemperature.Value.ToString("0", CultureInfo.InvariantCulture) + "°C" : "不可用";
            string gpu = hardware.GpuTemperature.HasValue ? hardware.GpuTemperature.Value.ToString("0", CultureInfo.InvariantCulture) + "°C" : "不可用";
            using (Font labelFont = UiFont(8.1F, FontStyle.Regular))
            using (Font valueFont = UiFont(10.2F, FontStyle.Bold))
            using (Brush label = new SolidBrush(Color.FromArgb(161, 187, 208)))
            {
                g.DrawString("CPU", labelFont, label, 23, 63);
                g.DrawString("GPU", labelFont, label, 139, 63);
                using (Brush cpuBrush = new SolidBrush(TemperatureColor(hardware.CpuTemperature))) g.DrawString(cpu, valueFont, cpuBrush, 61, 59);
                using (Brush gpuBrush = new SolidBrush(TemperatureColor(hardware.GpuTemperature))) g.DrawString(gpu, valueFont, gpuBrush, 177, 59);
            }
            string memory = "物理内存  " + (hardware.MemoryPercent.HasValue ? hardware.MemoryPercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "不可用");
            if (hardware.MemoryUsedGiB.HasValue && hardware.MemoryTotalGiB.HasValue)
                memory += "  " + hardware.MemoryUsedGiB.Value.ToString("0.#", CultureInfo.InvariantCulture) + "/" + hardware.MemoryTotalGiB.Value.ToString("0.#", CultureInfo.InvariantCulture) + " GB";
            using (Font font = UiFont(8F, FontStyle.Regular))
            using (Brush text = new SolidBrush(Color.FromArgb(216, 231, 244))) g.DrawString(memory, font, text, 23, 82);
            DrawSecondaryMessage(g, !hardware.CpuTemperature.HasValue ? "CPU 未连接 · 右键查看数据来源" : CompactCpuSource(hardware.CpuSensorSource) + " · 更新 " + ShanghaiTime(hardware.UpdatedAt).ToString("HH:mm:ss", CultureInfo.InvariantCulture), 102);
        }

        private void DrawMainMessage(Graphics g, string message, float y)
        {
            using (Font font = UiFont(9F, FontStyle.Regular))
            using (Brush text = new SolidBrush(Color.FromArgb(223, 234, 245)))
            using (StringFormat format = LeftFormat()) g.DrawString(message, font, text, new RectangleF(23, y, 225, 20), format);
        }

        private void DrawSecondaryMessage(Graphics g, string message, float y)
        {
            using (Font font = UiFont(6.8F, FontStyle.Regular))
            using (Brush text = new SolidBrush(Color.FromArgb(142, 165, 185)))
            using (StringFormat format = LeftFormat()) g.DrawString(message, font, text, new RectangleF(23, y - 2, 225, 13), format);
        }

        private void DrawWeatherEffects(Graphics g, long now, bool foreground)
        {
            if (activeHoliday != null) return;
            string condition = CurrentCondition();
            int severity = CurrentSeverity();
            float time = now / 1000F;
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(14, 118, 244, 211));
            if (condition == "rain")
            {
                int count = foreground ? 3 * severity : 5 * severity;
                using (Pen rain = new Pen(Color.FromArgb(foreground ? 125 : 85, 132, 206, 246), severity == 3 ? 1.5F : 1.1F))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float x = 16 + (i * 41.3F + (foreground ? 10 : 0)) % 232;
                        float y = 118 + ((i * 57F + time * (85 + severity * 30)) % 211);
                        g.DrawLine(rain, x, y, x - severity * 2, y + 8 + severity * 3);
                    }
                }
            }
            else if (condition == "snow")
            {
                int count = foreground ? 2 * severity : 5 * severity;
                using (Brush snow = new SolidBrush(Color.FromArgb(foreground ? 180 : 145, 228, 245, 253)))
                {
                    for (int i = 0; i < count; i++)
                    {
                        float x = 20 + (i * 45F) % 227 + (float)Math.Sin(time * 1.1F + i) * 10;
                        float y = 119 + ((i * 37F + time * (17 + severity * 10)) % 210);
                        float r = 2.2F + i % 3;
                        g.FillEllipse(snow, x, y, r, r);
                    }
                }
            }
            else if (condition == "wind")
            {
                int count = foreground ? severity * 2 : severity * 6;
                for (int i = 0; i < count; i++)
                {
                    float phase = (time * (24 + severity * 24) + i * 43.7F) % 280;
                    float x = phase - 8;
                    float y = 132 + (i * 37 + (foreground ? 23 : 0)) % 170
                        + (float)Math.Sin(time * (1 + severity * 0.4F) + i) * (4 + severity * 2);
                    // Foreground particles stay outside the face and laptop area.
                    if (foreground && x > 72 && x < 207) continue;
                    DrawWindParticle(g, x, y, 3 + severity * 0.8F + i % 2,
                        time * (38 + severity * 25) + i * 71, i % 3 != 0, foreground ? 210 : 170);
                }
            }
            else if (condition == "sun" && !foreground && (previewCondition != null || weather == null || weather.IsDay != false))
            {
                float pulse = 0.75F + (float)Math.Sin(time * 1.3F) * 0.2F;
                using (Pen sparkle = new Pen(AlphaColor((int)(90 * pulse), 252, 215, 140), 1.4F))
                {
                    DrawSparkle(g, sparkle, 37, 145, 4 + severity);
                    DrawSparkle(g, sparkle, 235, 193, 3 + severity);
                    if (severity > 1) DrawSparkle(g, sparkle, 217, 131, 4);
                }
            }
            g.Restore(state);
        }

        private static void DrawSparkle(Graphics g, Pen pen, float x, float y, float size)
        {
            g.DrawLine(pen, x - size, y, x + size, y);
            g.DrawLine(pen, x, y - size, x, y + size);
        }

        private static void DrawWindParticle(Graphics graphics, float x, float y, float size, float angle, bool petal, int alpha)
        {
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(x, y); graphics.RotateTransform(angle);
            graphics.ScaleTransform(size, size);
            using (GraphicsPath shape = new GraphicsPath())
            using (Brush fill = new SolidBrush(petal ? AlphaColor(alpha, 250, 173, 199) : AlphaColor(alpha, 188, 158, 77)))
            using (Pen vein = new Pen(petal ? AlphaColor(alpha / 2, 206, 112, 157) : AlphaColor(alpha / 2, 113, 127, 62), 0.12F))
            {
                shape.AddBezier(-1, 0, -0.8F, -0.8F, 0.55F, -0.75F, 1, 0);
                shape.AddBezier(1, 0, 0.15F, 0.85F, -0.7F, 0.65F, -1, 0);
                shape.CloseFigure(); graphics.FillPath(fill, shape);
                graphics.DrawLine(vein, -0.8F, 0, 0.75F, 0);
            }
            graphics.Restore(state);
        }

        private void DrawLaptop(Graphics graphics, long now)
        {
            GraphicsState transitionState = graphics.Save();
            try
            {
                ApplyLaptopTransition(graphics, now);
            Image laptop;
            // The lid covers the illustration's resting hands and held food.
            RectangleF laptopBounds = new RectangleF(58, 220, 156, 104);
            bool rearView = images.TryGetValue("props/laptop-back", out laptop);
            if (rearView) laptopBounds = new RectangleF(56, 217, 136, 83);
            if (rearView) { if (DrawTypingAt(now)) DrawTypingHands(graphics, now, laptop as Bitmap, laptopBounds); }
            else images.TryGetValue("props/laptop", out laptop);
            if (laptop != null) graphics.DrawImage(laptop, laptopBounds);
            else
            {
                using (Brush screen = new SolidBrush(Color.FromArgb(55, 73, 99)))
                using (Brush glass = new SolidBrush(Color.FromArgb(114, 199, 220)))
                using (Brush keyboard = new SolidBrush(Color.FromArgb(181, 197, 211)))
                {
                    graphics.FillRectangle(screen, 78, 226, 116, 68);
                    graphics.FillRectangle(glass, 84, 232, 104, 56);
                    graphics.FillPolygon(keyboard, new PointF[] { new PointF(78, 294), new PointF(194, 294), new PointF(210, 320), new PointF(62, 320) });
                }
            }
            if (rearView) return;
            if (DrawTypingAt(now)) DrawTypingHands(graphics, now, null, laptopBounds);
            using (Pen keyLight = new Pen(AlphaColor((int)(120 + 70 * Math.Sin(now / 100.0)), 144, 228, 247), 1.3F))
            {
                graphics.DrawLine(keyLight, 119, 308, 135, 308);
                graphics.DrawLine(keyLight, 143, 308, 153, 308);
            }
        
            }
            finally { graphics.Restore(transitionState); }
        }

        private void DrawTypingHands(Graphics graphics, long now, Bitmap rearLid, RectangleF laptopBounds)
        {
            float leftTap = (float)(Math.Sin(now / 85.0) * 2.6);
            float rightTap = (float)(Math.Sin(now / 85.0 + Math.PI) * 2.6);
            float leftY = rearLid == null ? 292 : LaptopTopAt(rearLid, laptopBounds, 106) - 4;
            float rightY = rearLid == null ? 292 : LaptopTopAt(rearLid, laptopBounds, 161) - 4;
            using (Brush cuff = new SolidBrush(WorkingCuffColor()))
            using (Brush hands = new SolidBrush(Color.FromArgb(255, 226, 211)))
            using (Pen outline = new Pen(Color.FromArgb(107, 79, 97), 0.8F))
            {
                RectangleF left = new RectangleF(99, leftY + leftTap, 18, 10);
                RectangleF right = new RectangleF(154, rightY + rightTap, 18, 10);
                graphics.FillEllipse(cuff, new RectangleF(left.X - 3, left.Y + 2, 11, 11));
                graphics.FillEllipse(cuff, new RectangleF(right.X + 11, right.Y + 2, 11, 11));
                graphics.FillEllipse(hands, left); graphics.DrawEllipse(outline, left);
                graphics.FillEllipse(hands, right); graphics.DrawEllipse(outline, right);
            }
        }

        private static float LaptopTopAt(Bitmap bitmap, RectangleF bounds, float x)
        {
            int pixelX = Clamp((int)((x - bounds.X) * bitmap.Width / bounds.Width), 0, bitmap.Width - 1);
            for (int y = 0; y < bitmap.Height; y++)
                if (bitmap.GetPixel(pixelX, y).A > 160) return bounds.Y + y * bounds.Height / bitmap.Height;
            return bounds.Y + 12;
        }

        private Color WorkingCuffColor()
        {
            if (SolarTermCalendar.IsTermOutfit(activeHoliday)) {
                int[] rgb = SolarTermCalendar.GetCuffRgb(activeHoliday);
                return Color.FromArgb(rgb[0], rgb[1], rgb[2]);
            }
            switch (activeHoliday)
            {
                case "springfestival": case "lantern": case "christmas": return Color.FromArgb(162, 54, 71);
                case "qingming": case "dragonboat": return Color.FromArgb(68, 119, 106);
                case "qixi": return Color.FromArgb(146, 113, 164);
                case "midautumn": case "chongyang": return Color.FromArgb(190, 156, 89);
                case "laba": return Color.FromArgb(162, 83, 115);
                case "halloween": return Color.FromArgb(86, 67, 113);
                default: return Color.FromArgb(71, 91, 136);
            }
        }

        private void DrawChristmasGifts(Graphics graphics)
        {
            Image gifts;
            if (images.TryGetValue("christmas/gifts", out gifts))
            {
                graphics.DrawImage(gifts, LeftGiftBounds);
                graphics.DrawImage(gifts, RightGiftBounds);
            }
        }

        private void DrawFeeding(Graphics g, long elapsed)
        {
            if (elapsed < 1100)
            {
                float progress = Math.Min(1, elapsed / 900F);
                float x = 234 - progress * 102;
                float y = 238 - (float)Math.Sin(progress * Math.PI) * 68 - progress * 27;
                Image food;
                if (TryCurrentFoodImage(out food))
                {
                    float foodScale = 1 - Math.Max(0, progress - 0.75F) * 1.8F;
                    DrawFoodImage(g, food, x, y, foodScale);
                }
                else
                {
                using (Brush body = new SolidBrush(Color.FromArgb(245, 164, 91)))
                using (Brush highlight = new SolidBrush(Color.FromArgb(255, 215, 155)))
                using (Pen outline = new Pen(Color.FromArgb(124, 76, 63), 1F))
                {
                    g.FillPolygon(body, new PointF[] { new PointF(x + 16, y), new PointF(x + 27, y - 7), new PointF(x + 27, y + 7) });
                    g.FillEllipse(body, x, y - 8, 21, 16);
                    g.DrawEllipse(outline, x, y - 8, 21, 16);
                    g.FillEllipse(highlight, x + 4, y + 1, 11, 4);
                    g.FillEllipse(Brushes.DarkSlateGray, x + 4, y - 3, 2.5F, 2.5F);
                }
                }
            }

            if (elapsed > 3300)
            {
                float rise = (elapsed - 3300) / 35F;
                int alpha = Clamp(255 - (int)((elapsed - 3600) / 3), 0, 255);
                using (Brush heart = new SolidBrush(AlphaColor(alpha, 255, 149, 166)))
                using (Font font = UiFont(16F, FontStyle.Bold)) g.DrawString("♥", font, heart, 182, 171 - rise);
            }
        }

        private static void DrawFoodImage(Graphics graphics, Image food, float x, float y, float scale)
        {
            graphics.DrawImage(food, new RectangleF(x - 15 * scale, y - 18 * scale, 40 * scale, 36 * scale));
        }

        private void DrawMissingPet(Graphics g, float bob)
        {
            using (Brush body = new SolidBrush(Color.FromArgb(78, 127, 181))) g.FillEllipse(body, 58, 154 + bob, 158, 133);
            using (Brush belly = new SolidBrush(Color.FromArgb(215, 237, 248))) g.FillEllipse(belly, 79, 217 + bob, 117, 58);
            g.FillEllipse(Brushes.White, 93, 190 + bob, 23, 27);
            g.FillEllipse(Brushes.White, 159, 190 + bob, 23, 27);
            g.FillEllipse(Brushes.MidnightBlue, 100, 195 + bob, 12, 17);
            g.FillEllipse(Brushes.MidnightBlue, 161, 195 + bob, 12, 17);
            using (Font font = UiFont(8F, FontStyle.Regular))
            using (Brush text = new SolidBrush(Color.FromArgb(237, 246, 251)))
            using (StringFormat format = CenterFormat()) g.DrawString("请保留完整 assets 文件夹", font, text, new RectangleF(39, 305, 194, 20), format);
        }

        private void RenderWindow()
        {
            if (!ready || closed || !IsHandleCreated) return;
            long now = animationClock.ElapsedMilliseconds;
            using (Bitmap bitmap = RenderFrame(now)) { if (interactiveCheck) Invalidate(); else ApplyLayeredBitmap(bitmap); RecordInteractionFrame(bitmap, now); }
        }

        private void ApplyLayeredBitmap(Bitmap bitmap)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) { LastRenderError = "透明窗口绘图资源暂不可用。"; return; }
            IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero)
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
                LastRenderError = "透明窗口绘图资源暂不可用。";
                return;
            }
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
                oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);
                NativeMethods.NativePoint destination = new NativeMethods.NativePoint(Left, Top);
                NativeMethods.NativeSize size = new NativeMethods.NativeSize(bitmap.Width, bitmap.Height);
                NativeMethods.NativePoint source = new NativeMethods.NativePoint(0, 0);
                NativeMethods.BlendFunction blend = new NativeMethods.BlendFunction();
                blend.BlendOp = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = 1;
                bool success = NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2);
                if (success) HasRenderedLayeredFrame = true;
                LastRenderError = success ? null : "透明窗口更新失败，系统错误码 " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + "。";
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void LoadAssets()
        {
            string assets = Path.Combine(applicationDirectory, "assets");
            TryLoadImage("base", Path.Combine(assets, "base.png"));
            string[] categories = new string[] { "sun", "wind", "rain", "snow" };
            foreach (string category in categories)
            {
                for (int severity = 1; severity <= 3; severity++)
                {
                    string name = category + "-" + severity.ToString(CultureInfo.InvariantCulture);
                    TryLoadImage("weather/" + name, Path.Combine(assets, "weather", name + ".png"));
                }
            }
            LoadFrames(assets, "idle", 6);
            LoadFrames(assets, "walk-right", 8);
            LoadFrames(assets, "walk-left", 8);
            LoadFrames(assets, "feed", 6);
            foreach (string holidayId in HolidayIds) TryLoadImage("holidays/" + holidayId, Path.Combine(assets, "holidays", holidayId + ".png"));
            foreach (string holidayId in HolidayIds)
            {
                TryLoadImage("food/" + holidayId, Path.Combine(assets, "food", holidayId + ".png"));
                LoadLoop(assets, holidayId);
            }
            foreach (string termId in SolarTermCalendar.Ids) {
                string outfit = "term-" + termId;
                TryLoadImage("holidays/" + outfit, Path.Combine(assets, "solar-terms", termId + ".png"));
                TryLoadImage("food/" + outfit, Path.Combine(assets, "food", outfit + ".png"));
                LoadLoop(assets, outfit);
            }
            LoadLoop(assets, "base");
            LoadLoop(assets, "sun-3-work");
            TryLoadImage("weather/sun-3-work", Path.Combine(assets, "weather", "sun-3-work.png"));
            foreach (string category in categories) for (int severity = 1; severity <= 3; severity++) LoadLoop(assets, category + "-" + severity);
            foreach (string prop in new string[] { "laptop", "laptop-back", "umbrella", "fan", "muffler" }) TryLoadImage("props/" + prop, Path.Combine(assets, "props", prop + ".png"));
            TryLoadImage("christmas/gifts", Path.Combine(assets, "christmas", "gifts.png"));
            LoadActionAssets(assets);
            if (!images.ContainsKey("base")) assetWarning = "基础形象文件 assets/base.png 未找到。";
        }

        private void LoadFrames(string assets, string group, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string name = "frame-" + i.ToString("00", CultureInfo.InvariantCulture);
                TryLoadImage(group + "/" + name, Path.Combine(assets, group, name + ".png"));
            }
        }

        private void LoadLoop(string assets, string key)
        {
            for (int frame = 0; frame < 4; frame++)
            {
                string name = "frame-" + frame.ToString("00", CultureInfo.InvariantCulture);
                TryLoadImage("loops/" + key + "/" + name, Path.Combine(assets, "loops", key, name + ".png"));
            }
        }

        private void TryLoadImage(string key, string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (Image original = Image.FromStream(stream)) images[key] = new Bitmap(original);
            }
            catch (Exception ex) { assetWarning = "图片未能载入：" + Path.GetFileName(path) + "（" + ShortError(ex.Message) + "）"; }
        }

        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0) values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
                int x;
                int y;
                int parsed;
                string value;
                if (values.TryGetValue("x", out value) && Int32.TryParse(value, out x) && values.TryGetValue("y", out value) && Int32.TryParse(value, out y))
                {
                    Location = new Point(x, y);
                    hasSavedLocation = true;
                }
                if (values.TryGetValue("size", out value) && Int32.TryParse(value, out parsed) && (parsed == 80 || parsed == 100 || parsed == 125)) sizePercent = parsed;
                if (values.TryGetValue("display", out value) && Int32.TryParse(value, out parsed) && (parsed == 1 || parsed == 2)) displayMode = parsed;
                if (values.TryGetValue("fish", out value) && Int32.TryParse(value, out parsed)) fishCount = Math.Max(0, parsed);
                if (values.TryGetValue("wander", out value)) wanderEnabled = value == "1";
                if (values.TryGetValue("topmost", out value)) TopMost = value != "0";
                if (values.TryGetValue("holiday", out value) && (value == "auto" || value == "off" || Array.IndexOf(HolidayIds, value) >= 0)) holidayMode = value;
                if (values.TryGetValue("solarterm", out value) && (value == "auto" || value == "off" || SolarTermCalendar.IsValidId(value))) solarTermMode = value;
                LoadLifeSettings(values);
                string normalized;
                if (values.TryGetValue("petname", out value) && PetNameDialog.TryNormalize(value, out normalized)) petName = normalized;
                double latitude, longitude;
                string locationName, latitudeText, longitudeText;
                if (values.TryGetValue("weathername", out locationName) && values.TryGetValue("weatherlatitude", out latitudeText) && values.TryGetValue("weatherlongitude", out longitudeText)
                    && Double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
                    && Double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
                {
                    string admin1, admin2, country;
                    values.TryGetValue("weatheradmin1", out admin1); values.TryGetValue("weatheradmin2", out admin2); values.TryGetValue("weathercountry", out country);
                    try { services.SetWeatherLocation(new WeatherLocationData { Name = locationName, Admin1 = admin1, Admin2 = admin2, Country = country, Latitude = latitude, Longitude = longitude }); }
                    catch (ArgumentException) { /* Invalid persisted coordinates retain the default city. */ }
                }
            }
            catch { /* Corrupt or read-only settings should never prevent startup. */ }
        }

        private void SaveSettings()
        {
            try
            {
                WeatherLocationData location = services.CurrentWeatherLocation;
                string[] lines = new string[]
                {
                    "x=" + Left.ToString(CultureInfo.InvariantCulture),
                    "y=" + Top.ToString(CultureInfo.InvariantCulture),
                    "size=" + sizePercent.ToString(CultureInfo.InvariantCulture),
                    "display=" + displayMode.ToString(CultureInfo.InvariantCulture),
                    "fish=" + fishCount.ToString(CultureInfo.InvariantCulture),
                    "wander=" + (wanderEnabled ? "1" : "0"),
                    "topmost=" + (TopMost ? "1" : "0"),
                    "holiday=" + holidayMode,
                    "solarterm=" + solarTermMode,
                    "petname=" + petName,
                    "weathername=" + SettingText(location.Name),
                    "weatheradmin1=" + SettingText(location.Admin1),
                    "weatheradmin2=" + SettingText(location.Admin2),
                    "weathercountry=" + SettingText(location.Country),
                    "weatherlatitude=" + location.Latitude.ToString("R", CultureInfo.InvariantCulture),
                    "weatherlongitude=" + location.Longitude.ToString("R", CultureInfo.InvariantCulture)
                };
                lines = AppendLifeSettings(lines);
                File.WriteAllLines(settingsPath, lines, new UTF8Encoding(false));
                lastSavedAt = DateTime.Now;
            }
            catch { /* Portable applications can also be run from read-only media. */ }
        }

        private static string SettingText(string value)
        {
            StringBuilder text = new StringBuilder();
            foreach (char character in value ?? "") if (!Char.IsControl(character) && character != '\u2028' && character != '\u2029') text.Append(character);
            return text.ToString().Trim();
        }

        private void ClampToScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Clamp(Left, area.Left, area.Right - Width), Clamp(Top, area.Top, area.Bottom - Height));
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (closed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)delegate { if (!closed) { ClampToScreen(); RenderWindow(); SaveSettings(); } });
            }
            catch (InvalidOperationException) { }
        }

        private void ShowDiagnostics()
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine(petName + " · 数据来源与状态");
            report.AppendLine("天气地点：" + services.CurrentWeatherLocation.DisplayName);
            report.AppendLine("天气：Open-Meteo 当前天气，约每 10 分钟更新。");
            if (weather != null)
            {
                report.AppendLine("天气状态：" + (weather.IsStale ? "缓存" : "实时") + "；更新于 " + LocalTimestamp(weather.UpdatedAt));
                if (weather.WindSpeed.HasValue) report.AppendLine("风速：" + weather.WindSpeed.Value.ToString("0.#", CultureInfo.InvariantCulture) + " km/h");
                if (weather.Precipitation.HasValue) report.AppendLine("降水：" + weather.Precipitation.Value.ToString("0.#", CultureInfo.InvariantCulture) + " mm");
                if (!String.IsNullOrWhiteSpace(weather.Error)) report.AppendLine("天气提示：" + ShortError(weather.Error));
            }
            report.AppendLine();
            report.AppendLine("额度：当前 Codex 登录账户，通过本机 Codex App Server 读取；约每 2 分钟更新。");
            report.AppendLine("剩余百分比 = 100 − 已用百分比。额度窗口相互独立。");
            if (quota != null)
            {
                report.AppendLine("额度状态：" + (quota.IsStale ? "缓存" : "最近读取") + "；更新于 " + LocalTimestamp(quota.UpdatedAt));
                if (quota.Windows != null)
                {
                    foreach (QuotaWindow window in quota.Windows)
                    {
                        report.Append(window.Label + "：");
                        report.Append(window.RemainingPercent.HasValue ? window.RemainingPercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "% 剩余" : "未知");
                        if (window.ResetAt.HasValue) report.Append("；重置 " + LocalTimestamp(window.ResetAt.Value));
                        if (!String.IsNullOrWhiteSpace(window.Credits)) report.Append("；" + window.Credits);
                        report.AppendLine();
                    }
                }
                if (!String.IsNullOrWhiteSpace(quota.Error)) report.AppendLine("额度提示：" + ShortError(quota.Error));
            }
            report.AppendLine();
            report.AppendLine("后台查询：" + (BackgroundPollingSuspended ? "已暂停（关机、注销或休眠）" : "运行中；休眠自动暂停，唤醒自动恢复"));
            report.AppendLine("硬件：约每 5 秒读取；内存为物理内存占用。");
            report.AppendLine("GPU：直接调用 NVIDIA NVML 驱动库，不启动显卡温度查询子进程。");
            if (hardware != null)
            {
                if (!String.IsNullOrWhiteSpace(hardware.GpuName)) report.AppendLine("显卡：" + hardware.GpuName);
                report.AppendLine("GPU 温度来源：" + (hardware.GpuSensorSource ?? "尚未读取"));
                report.AppendLine("CPU 温度来源：" + (String.IsNullOrWhiteSpace(hardware.CpuSensorSource) ? "未找到可用传感器，不推算温度" : hardware.CpuSensorSource));
                if (!String.IsNullOrWhiteSpace(hardware.Error)) report.AppendLine("硬件提示：" + ShortError(hardware.Error));
            }
            report.AppendLine();
            report.AppendLine("Codex 工作状态：" + ActivityLabel + "；" + activitySource);
            try { report.AppendLine("开机启动：" + (StartupManager.IsEnabled(Application.ExecutablePath) ? "已启用" : "未启用")); } catch (Exception) { report.AppendLine("开机启动：暂不可读取"); }
            report.AppendLine("投喂次数：" + fishCount.ToString(CultureInfo.InvariantCulture) + " 次；当前食物：" + CurrentFoodName());
            report.AppendLine("当前循环动画：" + CurrentLoopKey() + "；" + (HasLoop(CurrentLoopKey()) ? "4 帧动画已加载" : "使用原有待机动画，新的四帧素材未齐"));
            report.AppendLine("拖动移动并散步；双击投喂；右键切换互斥的信息显示。");
            report.AppendLine("节日装扮：" + (holidayMode == "auto" ? "按北京时间自动" : holidayMode == "off" ? "已关闭" : "手动预览") + (activeHoliday == null ? "，当前无节日" : "，" + HolidayCalendar.GetName(activeHoliday)));
            report.AppendLine("节气装扮：" + (solarTermMode == "auto" ? "节气当天按北京时间自动；节日优先" : solarTermMode == "off" ? "已关闭" : "手动预览 " + SolarTermCalendar.GetName(solarTermMode)));
            if (previewCondition != null) report.AppendLine("当前为外观预览。右键 → 天气外观预览 → 跟随所在地实时天气，可恢复自动外观。");
            if (!String.IsNullOrWhiteSpace(assetWarning)) report.AppendLine("资源提示：" + assetWarning);
            if (!String.IsNullOrWhiteSpace(LastRenderError)) report.AppendLine("窗口提示：" + LastRenderError);
            if (lastSavedAt != default(DateTime)) report.AppendLine("设置保存于 " + LocalTimestamp(lastSavedAt));
            MessageBox.Show(this, report.ToString(), petName + " · 数据来源与状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string LocalTimestamp(DateTime time)
        {
            return time == default(DateTime) ? "未知" : ShanghaiTime(time).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static DateTime ShanghaiTime(DateTime time)
        {
            DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
            try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")); }
            catch (TimeZoneNotFoundException) { return utc.AddHours(8); }
            catch (InvalidTimeZoneException) { return utc.AddHours(8); }
        }

        private static string WeatherName(string condition, int severity)
        {
            string[] labels;
            if (condition == "wind") labels = new string[] { "微风", "大风", "强风" };
            else if (condition == "rain") labels = new string[] { "小雨", "中雨", "暴雨" };
            else if (condition == "snow") labels = new string[] { "小雪", "中雪", "大雪" };
            else labels = new string[] { "晴朗", "强日照", "酷热" };
            return labels[Clamp(severity, 1, 3) - 1];
        }

        private static Color TemperatureColor(double? temperature)
        {
            if (!temperature.HasValue) return Color.FromArgb(166, 183, 201);
            if (temperature.Value >= 85) return Color.FromArgb(255, 141, 145);
            if (temperature.Value >= 70) return Color.FromArgb(248, 204, 130);
            return Color.FromArgb(127, 217, 228);
        }

        private static string CompactQuotaLabel(QuotaWindow window, int index)
        {
            if (window.WindowMinutes.HasValue)
            {
                int minutes = window.WindowMinutes.Value;
                if (minutes == 10080) return "每周窗口";
                if (minutes == 1440) return "每日窗口";
                if (minutes > 0 && minutes % 60 == 0) return (minutes / 60).ToString(CultureInfo.InvariantCulture) + " 小时窗口";
                if (minutes > 0 && minutes < 60) return minutes.ToString(CultureInfo.InvariantCulture) + " 分钟窗口";
            }
            return String.IsNullOrWhiteSpace(window.Label) ? (index == 0 ? "主额度" : "次额度") : window.Label;
        }

        private static string CompactCpuSource(string source)
        {
            if (String.IsNullOrWhiteSpace(source)) return "CPU 传感器";
            if (source.IndexOf("ASUS", StringComparison.OrdinalIgnoreCase) >= 0) return "ASUS";
            if (source.IndexOf("AIDA64", StringComparison.OrdinalIgnoreCase) >= 0) return "AIDA64";
            if (source.IndexOf("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) >= 0) return "LibreHardwareMonitor";
            if (source.IndexOf("OpenHardwareMonitor", StringComparison.OrdinalIgnoreCase) >= 0) return "OpenHardwareMonitor";
            if (source.IndexOf("本地传感器助手", StringComparison.Ordinal) >= 0) return "本地传感器助手";
            int separator = source.IndexOf('/');
            string label = (separator < 0 ? source : source.Substring(0, separator)).Trim();
            return label.Length > 16 ? "CPU 传感器" : label;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (maximum < minimum) return minimum;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static string ShortError(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return "暂不可用";
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length > 240 ? value.Substring(0, 240) + "…" : value;
        }

        private static Font UiFont(float points, FontStyle style)
        {
            // The ARGB surface uses logical pixels and our own explicit scale.
            // Point fonts otherwise inherit desktop DPI and overflow the card.
            return new Font("Microsoft YaHei UI", points * 4F / 3F, style, GraphicsUnit.Pixel);
        }

        private static Color AlphaColor(int alpha, int red, int green, int blue)
        {
            return Color.FromArgb(Clamp(alpha, 0, 255), Clamp(red, 0, 255), Clamp(green, 0, 255), Clamp(blue, 0, 255));
        }

        private static void DrawFittedValue(Graphics graphics, string value, RectangleF rectangle, Color color)
        {
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Far;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.None;
                format.FormatFlags |= StringFormatFlags.NoWrap;
                float pixels = 11.2F;
                using (Font measureFont = new Font("Microsoft YaHei UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    float width = graphics.MeasureString(value, measureFont, Int32.MaxValue, format).Width;
                    if (width > rectangle.Width - 2) pixels = Math.Max(8, pixels * (rectangle.Width - 2) / width);
                }
                using (Font font = new Font("Microsoft YaHei UI", pixels, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Brush brush = new SolidBrush(color)) graphics.DrawString(value, font, brush, rectangle, format);
            }
        }

        private static StringFormat LeftFormat()
        {
            return new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        }

        private static StringFormat RightFormat()
        {
            return new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        }

        private static StringFormat CenterFormat()
        {
            return new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        }

        private static GraphicsPath RoundRectangle(RectangleF rectangle, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(Math.Min(rectangle.Width, rectangle.Height), radius * 2);
            if (diameter <= 0)
            {
                path.AddRectangle(rectangle);
                return path;
            }
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct NativePoint
            {
                public int X;
                public int Y;
                public NativePoint(int x, int y) { X = x; Y = y; }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct NativeSize
            {
                public int Width;
                public int Height;
                public NativeSize(int width, int height) { Width = width; Height = height; }
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct BlendFunction
            {
                public byte BlendOp;
                public byte BlendFlags;
                public byte SourceConstantAlpha;
                public byte AlphaFormat;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint destination, ref NativeSize size, IntPtr hdcSrc, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);
            [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
            [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
            [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
            [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
            [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
            [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
            [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        }
    }
}

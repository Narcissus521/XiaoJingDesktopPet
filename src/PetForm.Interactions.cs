using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private const int FeedingDuration = 4400;
        private const int ActionFrameCount = 22;
        private readonly ToolStripMenuItem patItem = new ToolStripMenuItem("摸摸头");
        private readonly ToolStripMenuItem careItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem randomLeisureItem = new ToolStripMenuItem("随机摸鱼与生活小习惯");
        private long pressStarted = -10000, liftStarted = -10000, landStarted = -10000, petStarted = -10000, stretchStarted = -10000;
        private long pendingPatAt = -1, suppressClickUntil = -1, finishQueuedAt = -1;
        private long lastPatAt = -10000, lastDragAt, rapidDragAt = -10000, closeLaptopStarted = -10000;
        private long workStarted = -10000, teaUntil = -10000, nextTeaAt, nextLeisureAt = 30000, leisureStarted = -10000;
        private long lookUntil = -10000, biteAt = -10000, caughtAt = -10000, nextCareDecayAt = 300000, fallStarted = -10000;
        private bool lifted, falling, pressCaughtFall, randomLeisure = true, pointerNear;
        private bool? lastConfirmedWorking;
        private int pendingClickKind, patStreak, mood = 70, fullness = 60, caughtType, leisureHomeX;
        private readonly int[] fishCollection = new int[6];
        private readonly Dictionary<string, PointF> fishingHotspots = new Dictionary<string, PointF>();
        private readonly HashSet<string> availableActionOutfits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> cachedActionOutfits = new List<string>();
        private string actionAssetsRoot;
        private static readonly string[] FishNames = { "小银鱼", "蓝尾鱼", "金鳞鱼", "泡泡鱼", "月光鱼", "星光鱼" };
        private string leisure = "";
        private PointF pressLogical;
        private double fallPosition, fallVelocity;

        private bool FeedingAt(long now) { return now >= feedStarted && now - feedStarted < FeedingDuration; }
        private bool PettingAt(long now) { return now >= petStarted && now - petStarted < 2200; }
        private bool StretchingAt(long now) { return now >= stretchStarted && now - stretchStarted < 2600; }
        private bool LandingAt(long now) { return now >= landStarted && now - landStarted < 1000; }
        private bool ClosingLaptopAt(long now) { return now >= closeLaptopStarted && now - closeLaptopStarted < 650; }
        private bool SippingAt(long now) { return activityBusy == true && now < teaUntil; }
        private bool InteractionBlocksWork(long now)
        { return AttentionActive || returnFlying || HomeReturnActive || lifted || falling || PettingAt(now) || StretchingAt(now) || LandingAt(now) || ClosingLaptopAt(now); }
        private PointF LogicalPoint(Point point) { return new PointF(point.X * 100F / sizePercent, point.Y * 100F / sizePercent); }
        private bool InPetArea(PointF point) { return point.Y >= 122 && point.Y < 330 && point.X >= 8 && point.X < 264; }
        private bool InHeadArea(PointF point) { return point.X >= 68 && point.X <= 203 && point.Y >= 145 && point.Y <= 229; }
        private string ActionOutfitKey()
        {
            string key = CurrentLoopKey();
            // These five scenes wear the same plain maid dress; weather particles
            // remain separate. Hats, coats and festival garments never use this alias.
            if (key == "sun-1" || key == "wind-1" || key == "wind-2" || key == "wind-3" || key == "rain-1") return "base";
            return key == "sun-3-work" ? "sun-3" : key;
        }
        private bool HasActionSet(string outfit) { return availableActionOutfits.Contains(outfit); }
        private static string ActionFrameName(int index) { return index < 20 ? "pose-" + index.ToString("00", CultureInfo.InvariantCulture) : "fall-" + (index - 20).ToString("00", CultureInfo.InvariantCulture); }
        private bool EnsureActionImages(string outfit)
        {
            if (!HasActionSet(outfit)) return false;
            if (cachedActionOutfits.Contains(outfit)) return true;
            for (int i = 0; i < ActionFrameCount; i++)
            {
                string name = ActionFrameName(i);
                TryLoadImage("actions/" + outfit + "/" + name, Path.Combine(actionAssetsRoot, outfit, name + ".png"));
            }
            cachedActionOutfits.Add(outfit);
            while (cachedActionOutfits.Count > 2)
            {
                string old = cachedActionOutfits[0]; cachedActionOutfits.RemoveAt(0);
                for (int i = 0; i < ActionFrameCount; i++) { string key = "actions/" + old + "/" + ActionFrameName(i); Image image; if (images.TryGetValue(key, out image)) { images.Remove(key); image.Dispose(); } }
            }
            return true;
        }
        private void StopLeisure(long now) { StopSceneAction(now); ReleaseAttention(); leisure = ""; biteAt = -10000; nextLeisureAt = now + 25000; }
        private void InteractionPress(Point location, long now)
        {
            PauseHomeWalkForInteraction();
            bool caughtFlight = CatchHomeFlight(now);
            pressLogical = LogicalPoint(location); pressStarted = now; lifted = false; pendingPatAt = -1; pressCaughtFall = falling;
            falling = false; fallVelocity = 0; lastDragAt = now; rapidDragAt = -10000;
            if (caughtFlight) BeginLift(now);
        }
        private void BeginLift(long now)
        {
            if (lifted || !InPetArea(pressLogical)) return;
            homeWalking = homeWalkPending = false;
            lifted = true; liftStarted = now; pendingPatAt = -1; petStarted = -10000;
            stretchStarted = -10000; closeLaptopStarted = -10000; feedStarted = -10000;
            wanderRemaining = 0; walkUntil = 0; falling = false; StopLeisure(now); UpdateMotionCadence(now);
        }
        private void ReportDragMotion(int dx, int dy, long now)
        {
            double elapsed = Math.Max(8, now - lastDragAt);
            double speed = Math.Sqrt((double)dx * dx + (double)dy * dy) * 1000 / elapsed;
            if (speed > 650 * sizePercent / 100.0) rapidDragAt = now;
            lastDragAt = now;
        }
        private void InteractionRelease(Point location, long now, bool didDrag, bool cancelled)
        {
            if (lifted)
            {
                lifted = false;
                falling = true; fallPosition = Top; fallVelocity = 0; fallStarted = now; landStarted = -10000;
            }
            else if (pressCaughtFall)
            { falling = true; fallPosition = Top; fallVelocity = 0; fallStarted = now; pendingPatAt = -1; }
            else if (!cancelled && !didDrag && now >= suppressClickUntil)
            {
                PointF p = LogicalPoint(location);
                pendingClickKind = InHeadArea(p) ? 1 : 0;
                if (pendingClickKind != 0) pendingPatAt = now + SystemInformation.DoubleClickTime;
            }
            if (falling) StartRandomRelease(now);
            pressStarted = -10000; pressCaughtFall = false; UpdateMotionCadence(now);
        }
        private void ApplyFallPhysics(long now, double elapsed)
        {
            if (!falling) return;
            int ground = DefaultHomePoint().Y;
            fallVelocity = Math.Min(950 * sizePercent / 100.0, fallVelocity + 1750 * elapsed * sizePercent / 100.0);
            fallPosition += fallVelocity * elapsed;
            if (fallPosition >= ground) {
                Top = ground; falling = false; landStarted = now; flightLandingStarted = -10000; walkUntil = 0;
                homeWalkPending = true; homeWalkAfter = now + 1000; nextLeisureAt = now + 18000; SaveSettings();
            } else Top = (int)Math.Round(fallPosition);
        }
        private void InteractionTick(long now)
        {
            TickSceneAction(now); UpdateAttention(now);
            if (leftPressed && !lifted && now - pressStarted >= 200 && InPetArea(pressLogical)) BeginLift(now);
            if (pendingPatAt >= 0 && now >= pendingPatAt)
            {
                pendingPatAt = -1;
                if (!leftPressed && !FeedingAt(now)) { PetHead(now); }
            }
            if (finishQueuedAt >= 0)
            {
                if (activityBusy == true || now - finishQueuedAt > 15000) finishQueuedAt = -1;
                else if (!leftPressed && !returnFlying && !HomeReturnActive && !falling && !FeedingAt(now) && !PettingAt(now) && !LandingAt(now))
                {
                    closeLaptopStarted = now; stretchStarted = now + 650; finishQueuedAt = -1;
                    wanderRemaining = 0; nextWanderAt = now + 15000; StopLeisure(now);
                }
            }
            if (now >= nextCareDecayAt)
            { nextCareDecayAt = now + 300000; fullness = Math.Max(30, fullness - 1); mood = Math.Max(40, mood - 1); }
            if (activityBusy == true)
            {
                if (now >= nextTeaAt && workStarted >= 0 && now - workStarted >= 900000 && !InteractionBlocksWork(now) && !FeedingAt(now))
                { teaUntil = now + TeaCycleTiming.WorkDuration; nextTeaAt = now + 900000; }
                return;
            }
            if (!String.IsNullOrEmpty(leisure) && now - leisureStarted > (leisure == "tea" ? SceneEntryDuration+TeaCycleTiming.Period*2 : 22000) && (fishingCycle == null || !fishingCycle.CatchActive)) { if(leisure == "tea") { if(!AttentionActive) BeginAttention(now); } else StopLeisure(now); }
            if (leisure == "leaf" && !leftPressed && !falling)
            {
                Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                double phase = (now - leisureStarted) % 8000;
                if (phase < 5000) Left = Clamp(leisureHomeX + (int)(Math.Sin(phase / 1100.0) * 30), area.Left, Math.Max(area.Left, area.Right - Width));
            }
            if (randomLeisure && activityBusy == false && String.IsNullOrEmpty(leisure) && now >= nextLeisureAt && !leftPressed && !falling && !FeedingAt(now) && !InteractionBlocksWork(now) && !petMenu.Visible && wanderRemaining <= 0 && HasActionSet(ActionOutfitKey()))
            {
                int hour = ShanghaiTime(DateTime.UtcNow).Hour;
                string[] choices = activeHoliday != null ? new string[] { "festival", "book", "bubbles", "fishing" }
                    : hour >= 22 || hour < 7 ? new string[] { "nap", "nap", "book", "bubbles" }
                    : CurrentCondition() == "rain" ? new string[] { "rain", "book", "fishing", "bubbles" }
                    : CurrentCondition() == "wind" ? new string[] { "leaf", "book", "fishing", "bubbles" }
                    : fullness < 40 || mood < 50 ? new string[] { "book", "nap", "tea", "bubbles" }
                    : new string[] { "fishing", "bubbles", "book", "nap", "sun" };
                StartLeisure(choices[random.Next(choices.Length)], now);
            }
        }
        private void ObserveWorkingTransition(bool wasWorking, long now)
        {
            if (activityBusy == true)
            {
                if (!wasWorking) { workStarted = now; nextTeaAt = now + 900000; }
                if (!String.IsNullOrEmpty(leisure)) StopLeisure(now); ReleaseAttention(); closeLaptopStarted = -10000;
            }
            else if (activityBusy == false) { workStarted = -10000; teaUntil = -10000; }
        }
        private void ObservePointer(long now)
        {
            if (leftPressed || activityBusy == true) return;
            Point p = PointToClient(Cursor.Position); PointF q = LogicalPoint(p);
            ObserveLogicalPointer(q, now);
        }
        private void ObserveLogicalPointer(PointF q, long now)
        {
            bool near = q.X >= 18 && q.X <= 254 && q.Y >= 125 && q.Y <= 328 && (leisure != "fishing" || q.X>=80 && q.X<=187 && q.Y>=180 && q.Y<=276);
            if (near && !pointerNear && !String.IsNullOrEmpty(leisure)) BeginAttention(now);
            pointerNear = near;
        }
        private void PetHead(long now)
        {
            if (returnFlying || lifted || falling || FeedingAt(now)) return;
            PauseHomeWalkForInteraction();
            patStreak = now - lastPatAt < 7000 ? patStreak + 1 : 1; lastPatAt = now;
            petStarted = now; pendingPatAt = -1; wanderRemaining = 0; walkUntil = 0;
            mood = Clamp(mood + 2, 0, 100); StopLeisure(now); nextWanderAt = now + 12000; RenderWindow();
        }
        private void StartLeisure(string action, long now)
        {
            if (returnFlying || activityBusy == true || lifted || falling || FeedingAt(now)) return;
            PauseHomeWalkForInteraction();
            StopLeisure(now); pointerNear = true; leisure = action; leisureStarted = now; leisureHomeX = Left; wanderRemaining = 0; walkUntil = 0;
            biteAt = action == "fishing" ? now + 5000 + random.Next(4500) : -10000;
            nextLeisureAt = now + 45000 + random.Next(25000); StartSceneAction(now); if (!SceneAction) { EnsureLeisureMotion(action); PrepareAttentionMorph(); } RenderWindow();
        }
        private bool FishingAt(long now) { return leisure == "fishing" && !AttentionActive && !lifted && !falling && now >= leisureStarted; }
        private PointF FloatPoint() { if (leisure == "fishing") return SceneFloatPoint(); PointF p; return fishingHotspots.TryGetValue(ActionOutfitKey(), out p) ? p : new PointF(222, 286); }
        private bool TryLifeClick(Point location, long now)
        {
            PointF p = LogicalPoint(location), f = FloatPoint();
            if (FishingAt(now) && Math.Abs(p.X - f.X) <= 18 && Math.Abs(p.Y - f.Y) <= 22)
            {
                suppressClickUntil = now + SystemInformation.DoubleClickTime; pendingPatAt = -1;
                HookSceneFish(now);
                RenderWindow(); return true;
            }
            return false;
        }
        private void BuildInteractionMenu()
        {
            patItem.Click += delegate { PetHead(animationClock.ElapsedMilliseconds); }; petMenu.Items.Add(patItem);
            ToolStripMenuItem idle = new ToolStripMenuItem("一起摸鱼");
            string[] ids = { "fishing", "bubbles", "book", "nap", "tea", "sun", "rain", "leaf", "festival" };
            string[] labels = { "一起钓鱼（浮漂抖动时点击）", "吹泡泡", "翻翻书", "抱靠垫打盹", "喝一口茶", "晒晒太阳", "伸手接雨", "追一片落叶", "节日小互动" };
            for (int i = 0; i < ids.Length; i++) { string action = ids[i]; ToolStripMenuItem item = new ToolStripMenuItem(labels[i]); item.Click += delegate { StartLeisure(action, animationClock.ElapsedMilliseconds); }; idle.DropDownItems.Add(item); }
            petMenu.Items.Add(idle);
            randomLeisureItem.Click += delegate { randomLeisure = !randomLeisure; if (!randomLeisure) StopLeisure(animationClock.ElapsedMilliseconds); UpdateLifeMenuChecks(); SaveSettings(); }; petMenu.Items.Add(randomLeisureItem);
            BuildHomeMenu();
            ToolStripMenuItem collection = new ToolStripMenuItem("查看我的鱼缸收藏"); collection.Click += delegate { using (FishCollectionForm dialog = new FishCollectionForm(images, fishCollection, FishNames)) dialog.ShowDialog(this); }; petMenu.Items.Add(collection);
            careItem.Enabled = false; petMenu.Items.Add(careItem);
            petMenu.Opening += delegate { idle.Enabled = activityBusy != true && !lifted && !falling; UpdateLifeMenuChecks(); careItem.Text = "心情 " + mood + " / 饱腹 " + fullness + " · 收藏 " + TotalFish() + " 条"; };
        }
        private void UpdateLifeMenuChecks()
        {
            randomLeisureItem.Checked = randomLeisure;
            randomLeisureItem.Text = "随机摸鱼与生活习惯：" + (randomLeisure ? "已开启" : "已关闭");
            randomLeisureItem.ToolTipText = randomLeisure ? "点击关闭随机摸鱼与生活习惯" : "点击开启随机摸鱼与生活习惯";
        }
        private void SetStartupMenuState(bool? enabled)
        {
            startupItem.Checked = enabled == true;
            startupItem.Text = "开机自启：" + (enabled.HasValue ? enabled.Value ? "已开启" : "已关闭" : "状态读取失败");
            startupItem.ToolTipText = enabled.HasValue ? enabled.Value ? "点击关闭开机自启" : "点击开启开机自启" : "暂时无法读取开机自启设置，请重试";
        }
        private int TotalFish() { int n = 0; foreach (int count in fishCollection) n += count; return n; }
        private int ActionPoseIndex(long now)
        {
            if (returnFlying) return 0;
            if (lifted) return now - rapidDragAt < 1000 ? 2 : 0;
            if (falling) return 20;
            if (LandingAt(now)) { long t = now - landStarted; return t < 320 ? 3 : 4; }
            if (homeWalking) return 0;
            if (PettingAt(now)) return patStreak >= 3 ? 7 : 6;
            if (FeedingAt(now)) { long t = now - feedStarted; if (CurrentCondition() == "sun" && CurrentSeverity() == 3 && t >= 1800 && t < 2150) return 2; return t < 850 ? 9 : t < 2450 ? 10 : t < 3300 ? 11 : 12; }
            if (ClosingLaptopAt(now)) return 12;
            if (StretchingAt(now)) return now - stretchStarted < 2100 ? 13 : 12;
            if (now - caughtAt >= 0 && now - caughtAt < 2600) return 12;
            if (SippingAt(now)) return 18;
            if (activityBusy == true) return now - workStarted >= 0 && now - workStarted < 500 ? 9 : -1;
            
            switch (leisure) { case "fishing": return 14; case "bubbles": return 15; case "book": return 16; case "nap": return 17; case "tea": return 18; case "rain": return 19; case "leaf": return (now - leisureStarted) % 8000 < 5000 ? 8 : (now - leisureStarted) % 8000 < 6500 ? 3 : 12; case "sun": return 12; case "festival": return activeHoliday == "qixi" ? 7 : (now - leisureStarted) % 6000 < 1800 ? 8 : (now - leisureStarted) % 6000 < 4000 ? 9 : 12; default: return -1; }
        }
        private Image InteractionImage(long now)
        { int pose = ActionPoseIndex(now); string outfit = ActionOutfitKey(); if (pose < 0 || !EnsureActionImages(outfit)) return null; Image image; return images.TryGetValue("actions/" + outfit + "/" + ActionFrameName(pose), out image) ? image : null; }
        private bool UsingActionArtwork(long now) { return InteractionImage(now) != null; }
        private bool InteractionPose(long now, ref float x, ref float y, ref float angle)
        {
            if (returnFlying) { y = (float)Math.Sin((now - flightStarted) / 150.0) * .8F; angle = 0; return true; }
            if (lifted) { x = (float)Math.Sin((now - liftStarted) / 230.0) * 3; y = -7; angle = (float)Math.Sin((now - liftStarted) / 270.0) * 5; return true; }
            if (falling) { x = (float)Math.Sin((now - fallStarted) / 150.0) * 1.2F; y = -5 * (1 - Math.Min(1, (now - fallStarted) / 180F)); angle = -2.5F; return true; }
            if (LandingAt(now)) { x = 0; y = (float)(Math.Sin((now - landStarted) / 90.0) * 5 * Math.Exp(-(now - landStarted) / 350.0)); angle = 0; return true; }
            if (homeWalking) { x = y = angle = 0; return true; }
            if (PettingAt(now)) { x = (float)Math.Sin(now / 230.0) * 2; angle = x; return true; }
            if (FeedingAt(now) && CurrentCondition() == "sun" && CurrentSeverity() == 3 && now - feedStarted >= 1800 && now - feedStarted < 2150) { x = (float)Math.Sin(now / 40.0) * 2; angle = 0; return true; }
            if (StretchingAt(now)) { y = -.5F; angle = 0; return true; }
            if (AttentionActive) { x = y = angle = 0; return true; }
            if (!String.IsNullOrEmpty(leisure)) { y = leisure == "fishing" && now >= biteAt && now - biteAt < 3400 ? (float)Math.Sin(now / 55.0) * 2 : (float)Math.Sin(now / 600.0) * .6F; angle = 0; return true; }
            return false;
        }
        private string InteractionHint(long now)
        {
            if (returnFlying) return "展开小龙翼，飞回屏幕右下角～";
            if (homeWalking) return "迈着小步，走回屏幕右下角～";
            if (lifted) return now - rapidDragAt < 1000 ? "哇～慢一点呀！" : "悬空摆摆腿～松手随机飞行或下落";
            if (falling) return "按住裙摆～双腿向后收！";
            if (LandingAt(now)) return now - landStarted < 320 ? "落地啦！" : "整理一下裙摆～";
            if (PettingAt(now)) return patStreak >= 3 ? "被摸得有点害羞啦 ♡" : "蹭蹭你的手～好舒服 ♡";
            if (ClosingLaptopAt(now)) return "忙完啦，合上电脑～";
            if (StretchingAt(now)) return "伸个懒腰，休息一下～";
            if (FeedingAt(now)) { long t = now - feedStarted; return t < 850 ? "伸手接住" + CurrentFoodName() + "～" : t < 2450 ? "鼓鼓脸颊，嚼嚼嚼～" : t < 3300 ? "擦擦嘴～" : "吃饱啦，谢谢你 ♡"; }
            if (now - caughtAt >= 0 && now - caughtAt < 2600) return "钓到" + FishNames[caughtType] + "！已放进收藏鱼缸";
            if (SippingAt(now)) return "工作久了，喝口茶再继续～";
            if (AttentionActive || now < lookUntil) return "你来啦～抬头看看你";
            string sceneHint = SceneActionHint(now); if (sceneHint != null) return sceneHint;
            switch (leisure) { case "fishing": return now >= biteAt && now - biteAt < 3400 ? "浮漂动啦！点右下角浮漂～" : "等鱼儿咬钩～浮漂抖动时点击"; case "book": return "翻翻书，陪你安静待一会儿～"; case "bubbles": return "呼～吹一串小泡泡"; case "nap": return "抱着靠垫，眯一小会儿…"; case "tea": return "捧起茶杯，喝一小口～"; case "rain": return "伸手接一滴雨～"; case "leaf": return "追一片被风吹走的落叶～"; case "sun": return "晒晒太阳，好暖和～"; case "festival": return "穿上今天的装扮，和你过节～"; default: return null; }
        }
        private void LoadActionAssets(string assets)
        {
            string root = Path.Combine(assets, "actions"); actionAssetsRoot = root;
            if (Directory.Exists(root)) foreach (string dir in Directory.GetDirectories(root))
            {
                bool ready = true;
                for (int i = 0; i < ActionFrameCount; i++) if (!File.Exists(Path.Combine(dir, ActionFrameName(i) + ".png"))) { ready = false; break; }
                if (ready) availableActionOutfits.Add(Path.GetFileName(dir));
            }
            if (Directory.Exists(root)) foreach (string dir in Directory.GetDirectories(root))
            {
                string hotspot = Path.Combine(dir, "hotspots.ini");
                if (!File.Exists(hotspot)) continue;
                try { string[] lines = File.ReadAllLines(hotspot); float x, y; if (lines.Length >= 2 && Single.TryParse(lines[0].Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && Single.TryParse(lines[1].Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) fishingHotspots[Path.GetFileName(dir)] = new PointF(x, y); } catch { }
            }
            foreach (string name in new string[] { "cushion", "bed", "aquarium", "cup", "icepop", "fish-0", "fish-1", "fish-2", "fish-3", "fish-4", "fish-5" }) TryLoadImage("life/" + name, Path.Combine(assets, "life", name + ".png"));
        }
        private void LoadLifeSettings(Dictionary<string, string> values)
        {
            string value; int n;
            if (values.TryGetValue("mood", out value) && Int32.TryParse(value, out n)) mood = Clamp(n, 0, 100);
            if (values.TryGetValue("fullness", out value) && Int32.TryParse(value, out n)) fullness = Clamp(n, 0, 100);
            if (values.TryGetValue("leisure", out value)) randomLeisure = value != "0";
            for (int i = 0; i < 6; i++) if (values.TryGetValue("collection" + i, out value) && Int32.TryParse(value, out n)) fishCollection[i] = Clamp(n, 0, 9999);
        }
        private string[] AppendLifeSettings(string[] lines)
        {
            List<string> result = new List<string>(lines); result.Add("mood=" + mood); result.Add("fullness=" + fullness); result.Add("leisure=" + (randomLeisure ? "1" : "0"));
            for (int i = 0; i < 6; i++) result.Add("collection" + i + "=" + fishCollection[i]); return result.ToArray();
        }
    }
}

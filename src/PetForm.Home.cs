using System;
using System.Drawing;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private bool returnFlying, homeWalking, homeWalkPending;
        private string homeScreenName;
        private Point flightFrom, flightTo;
        private long flightStarted = -10000, homeWalkStarted = -10000, homeWalkAfter, motionPausedAt = -1;
        private int flightDuration;
        private double homeWalkPosition, homeWalkSpeed;
        private double homeWalkStartX, homeWalkTotalDistance, homeWalkCycles;
        private int homeWalkPhase;
        private long homeWalkPhaseStarted;
        private bool HomeReturnActive { get { return homeWalking || homeWalkPending; } }
        private Screen HomeScreen()
        {
            foreach (Screen screen in Screen.AllScreens) if (screen.DeviceName == homeScreenName) return screen;
            Screen fallback = Screen.FromRectangle(Bounds); homeScreenName = fallback.DeviceName; return fallback;
        }
        private static Point HomePoint(Rectangle area, Size size, int percent)
        {
            int marginX = (int)Math.Round(28 * percent / 100F), marginY = (int)Math.Round(4 * percent / 100F);
            return new Point(Math.Max(area.Left, area.Right - size.Width - marginX), Math.Max(area.Top, area.Bottom - size.Height - marginY));
        }
        private Point DefaultHomePoint() { return HomePoint(HomeScreen().WorkingArea, ClientSize, sizePercent); }
        private void InitializeHomePosition()
        {
            homeScreenName = (hasSavedLocation ? Screen.FromRectangle(Bounds) : Screen.PrimaryScreen).DeviceName;
            Location = DefaultHomePoint();
        }
        private void BuildHomeMenu()
        {
            var item = new ToolStripMenuItem("返回屏幕右下角");
            item.ToolTipText = "默认地点位于当前屏幕的可用区域右下角，避开任务栏。松手时随机飞回，或捂裙摆下落后走回。";
            item.Click += delegate {
                if (lifted || leftPressed) return;
                homeScreenName = Screen.FromRectangle(Bounds).DeviceName;
                if (!StartHomeFlight(animationClock.ElapsedMilliseconds)) BeginFreeFall(animationClock.ElapsedMilliseconds);
            };
            petMenu.Items.Add(item);
        }
        private void PauseHomeWalkForInteraction()
        {
            if (!homeWalking) return;
            homeWalking = false; homeWalkPending = true; homeWalkSpeed = 0; walkUntil = 0;
        }
        private void StartRandomRelease(long now)
        {
            homeScreenName = Screen.FromRectangle(Bounds).DeviceName;
            homeWalking = homeWalkPending = false; wanderRemaining = 0; walkUntil = 0;
            // Choose exactly once per release, regardless of distance. Resume,
            // polling and animation ticks never reroll a falling character.
            if (random.Next(2) == 0 && StartHomeFlight(now)) return;
            BeginFreeFall(now);
        }
        private void BeginFreeFall(long now)
        {
            returnFlying = false; homeWalking = homeWalkPending = false;
            falling = true; lifted = false; fallPosition = Top; fallVelocity = 0;
            fallStarted = now; landStarted = flightLandingStarted = -10000;
            pendingPatAt = -1; feedStarted = closeLaptopStarted = stretchStarted = -10000;
            StopLeisure(now); wanderRemaining = 0; walkUntil = 0; UpdateMotionCadence(now);
        }
        private bool StartHomeFlight(long now)
        {
            flightFrom = Location; flightTo = DefaultHomePoint();
            double distance = Math.Sqrt(Math.Pow(flightTo.X-flightFrom.X, 2) + Math.Pow(flightTo.Y-flightFrom.Y, 2));
            if (distance < 2) { FinishHomeFlight(now, false); return true; }
            if (!PrepareFlightArtwork()) return false;
            returnFlying = true; falling = lifted = homeWalking = homeWalkPending = false;
            pendingPatAt = -1; feedStarted = closeLaptopStarted = stretchStarted = landStarted = -10000;
            StopLeisure(now); wanderRemaining = 0; walkUntil = 0;
            double targetDuration = Math.Max(2260, Math.Min(3940, 1600 + distance * 1.5));
            int loops = Math.Max(2, Math.Min(5, (int)Math.Ceiling((targetDuration-1140) / 560)));
            flightStarted = now; flightDuration = 1140 + loops * 560; motionPausedAt = -1;
            UpdateMotionCadence(now); return true;
        }
        private void FlightPathAt(double time, out PointF position, out PointF tangent)
        {
            double t = Math.Max(0, Math.Min(1, time)); t = t*t*(3-2*t); double q = 1-t;
            double dx = flightTo.X-flightFrom.X, dy = flightTo.Y-flightFrom.Y;
            double rise = Math.Min(80*sizePercent/100F, Math.Sqrt(dx*dx+dy*dy)*.14);
            double ax = flightFrom.X+dx*.28, ay = flightFrom.Y+dy*.28-rise;
            double bx = flightFrom.X+dx*.74, by = flightFrom.Y+dy*.74-rise*.45;
            position = new PointF((float)(q*q*q*flightFrom.X+3*q*q*t*ax+3*q*t*t*bx+t*t*t*flightTo.X), (float)(q*q*q*flightFrom.Y+3*q*q*t*ay+3*q*t*t*by+t*t*t*flightTo.Y));
            tangent = new PointF((float)(3*q*q*(ax-flightFrom.X)+6*q*t*(bx-ax)+3*t*t*(flightTo.X-bx)), (float)(3*q*q*(ay-flightFrom.Y)+6*q*t*(by-ay)+3*t*t*(flightTo.Y-by)));
        }
        private void FinishHomeFlight(long now, bool authoredLanding)
        {
            returnFlying = falling = homeWalking = homeWalkPending = false; Location = DefaultHomePoint(); landStarted = now;
            flightLandingStarted = authoredLanding ? now : -10000; fallVelocity = 0; walkUntil = 0;
            nextLeisureAt = now+18000; nextWanderAt = now+25000; SaveSettings();
        }
        private void UpdateHomeFlight(long now)
        {
            if (!returnFlying) return;
            flightTo = DefaultHomePoint();
            double time = Math.Max(0, Math.Min(1, (now-flightStarted)/(double)flightDuration)); PointF point,tangent;
            FlightPathAt(time, out point, out tangent); Rectangle area = SystemInformation.VirtualScreen;
            Location = new Point(Clamp((int)Math.Round(point.X),area.Left,Math.Max(area.Left,area.Right-Width)), Clamp((int)Math.Round(point.Y),area.Top,Math.Max(area.Top,area.Bottom-Height)));
            if (time >= 1) FinishHomeFlight(now,true);
        }
        private bool CatchHomeFlight(long now)
        {
            if (!returnFlying) return false;
            returnFlying = false; falling = false; fallVelocity = 0; fallPosition = Top; return true;
        }
        private void CompleteHomeWalk(long now)
        {
            Location = DefaultHomePoint(); homeWalking = homeWalkPending = false; walkUntil = 0; homeWalkSpeed = 0;
            nextLeisureAt = now+18000; nextWanderAt = now+25000; SaveSettings();
        }
        private void UpdateHomeWalk(long now, double elapsed)
        {
            if (returnFlying || lifted || falling || leftPressed || petMenu.Visible) return;
            if (!HomeReturnActive) return;
            if (pendingPatAt >= 0 || PettingAt(now) || FeedingAt(now) || StretchingAt(now) || !String.IsNullOrEmpty(leisure)) { PauseHomeWalkForInteraction(); return; }
            Point home = DefaultHomePoint(); double scale = sizePercent/100.0;
            if (!homeWalking) {
                if (now < homeWalkAfter || LandingAt(now)) return;
                double length = Math.Abs(home.X-Left);
                if (length <= .5) { CompleteHomeWalk(now); return; }
                walkDirection = home.X < Left ? -1 : 1;
                // Missing or incomplete artwork waits safely instead of
                // substituting a front-facing sliding figure.
                if (!PrepareSideWalkArtwork(walkDirection > 0)) return;
                homeWalking = true; homeWalkPending = false; homeWalkStarted = now;
                homeWalkPhase = 0; homeWalkPhaseStarted = now;
                homeWalkStartX = homeWalkPosition = Left; homeWalkSpeed = 0;
                homeWalkTotalDistance = length; homeWalkCycles = Math.Max(1,Math.Ceiling(length/(36*scale)));
                closeLaptopStarted = stretchStarted = -10000; StopLeisure(now); wanderRemaining = 0;
            }
            if (homeWalkPhase == 0) {
                if (now-homeWalkPhaseStarted < WalkTurnMilliseconds+WalkSettleMilliseconds) return;
                homeWalkPhase = 1; homeWalkPhaseStarted = now;
            }
            if (homeWalkPhase == 2) {
                if (now-homeWalkPhaseStarted >= WalkSettleMilliseconds+WalkTurnMilliseconds+80) CompleteHomeWalk(now);
                return;
            }
            double distance = home.X-homeWalkPosition;
            if (Math.Abs(distance) <= .5) {
                Location = home; homeWalkPosition = home.X; homeWalkSpeed = 0;
                homeWalkPhase = 2; homeWalkPhaseStarted = now; walkUntil = 0; return;
            }
            double dt = Math.Max(0,Math.Min(.1,elapsed));
            double desiredSpeed = Math.Min(60*scale,Math.Sqrt(2*250*scale*Math.Abs(distance)));
            homeWalkSpeed = Math.Min(desiredSpeed,homeWalkSpeed+300*scale*dt);
            double step = Math.Min(Math.Abs(distance),homeWalkSpeed*dt);
            homeWalkPosition += step*(distance < 0 ? -1 : 1);
            Location = new Point((int)Math.Round(homeWalkPosition),home.Y); walkUntil = now+100;
        }
        private void PauseHomeMotion(long now) { motionPausedAt = now; }
        private void ResumeHomeMotion(long now)
        {
            if (motionPausedAt < 0) return; long paused = Math.Max(0,now-motionPausedAt); motionPausedAt = -1;
            if (returnFlying) flightStarted += paused;
            if (homeWalking) { homeWalkStarted += paused; homeWalkPhaseStarted += paused; }
            if (homeWalkPending) homeWalkAfter += paused;
            if (landStarted >= 0) landStarted += paused;
            if (flightLandingStarted >= 0) flightLandingStarted += paused;
            if (falling) fallStarted += paused;
            previousTick = now;
        }
    }
}

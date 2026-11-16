using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private const int WorkSceneEntryDuration = 1100, WorkSceneExitDuration = 1100, WorkSceneRestDuration = 250;
        private SceneActionPlayer workScenePlayer;
        private long workTeaFrozenElapsed, workResumeAt = -1, workResumeOwner = -1;
        private Bitmap workFinishFrame;
        private long WorkCycleElapsed(long now)
        {
            long start = workResumeOwner == workStarted && workResumeAt >= 0 && now >= workResumeAt
                ? workResumeAt : workStarted + WorkSceneEntryDuration;
            return Math.Max(0, now - start);
        }
        private void BeginWorkTea(long now)
        {
            workTeaFrozenElapsed = WorkCycleElapsed(now);
            workResumeAt = teaUntil; workResumeOwner = workStarted;
        }
        private void ResetWorkTea()
        {
            workResumeAt = workResumeOwner = -1;
            if (workFinishFrame != null) { workFinishFrame.Dispose(); workFinishFrame = null; }
        }
        private void CaptureWorkFinish(long now)
        {
            if (workFinishFrame != null) { workFinishFrame.Dispose(); workFinishFrame = null; }
            bool drinking = SippingAt(now);
            if (!drinking && now - workStarted >= WorkSceneEntryDuration) return;
            using (var canvas = new Bitmap(272, 352, PixelFormat.Format32bppPArgb)) {
                using (var graphics = Graphics.FromImage(canvas)) {
                    if (!(drinking ? DrawWorkingTea(graphics, now) : DrawWorkScene(graphics, now))) return;
                }
                workFinishFrame = canvas.Clone(new Rectangle(0, 122, 272, 208), PixelFormat.Format32bppPArgb);
            }
        }
        private bool EnsureWorkScene()
        {
            string outfit = ActionOutfitKey();
            if (workScenePlayer != null && workScenePlayer.Outfit == outfit) return true;
            ReleaseWorkScene();
            try { workScenePlayer = new SceneActionPlayer(Path.Combine(actionAssetsRoot, "..", "scene-actions"), "work", outfit); return true; }
            catch (Exception error) { assetWarning = "工作场景未能载入：" + error.Message; return false; }
        }
        private bool DrawWorkScene(Graphics g, long now)
        {
            bool closing = ClosingLaptopAt(now);
            bool pendingFinish = finishQueuedAt >= 0 && !InteractionBlocksWork(now) && !FeedingAt(now);
            if ((!IsWorkVisual(now) && !closing && !pendingFinish) || SippingAt(now) || !EnsureWorkScene()) return false;
            long elapsed = Math.Max(0, (closing ? closeLaptopStarted : now) - workStarted);
            if ((closing || pendingFinish) && workFinishFrame != null) {
                using (var idle = SceneIdleTarget()) {
                    if (idle == null) return false;
                    DrawWorkTransition(g, workFinishFrame, idle, closing ? (now - closeLaptopStarted) / (double)WorkSceneExitDuration : 0, false);
                }
            } else if (closing) {
                long leaving = Math.Max(0, now - closeLaptopStarted);
                ScenePose pose = WorkCycleTiming.At(WorkCycleElapsed(closeLaptopStarted));
                Bitmap frame = workScenePlayer.Frame(pose, elapsed);
                if (leaving < WorkSceneRestDuration) {
                    // Freeze the exact last clear pose before the magical exit;
                    // switching to a separately drawn full scene made it flash.
                    g.DrawImageUnscaled(frame, 0, 122);
                } else using (var idle = SceneIdleTarget()) {
                    if (idle == null) return false;
                    DrawWorkTransition(g, frame, idle, (leaving - WorkSceneRestDuration) / (double)(WorkSceneExitDuration - WorkSceneRestDuration), false);
                }
            } else if (elapsed < WorkSceneEntryDuration) {
                using (var idle = SceneIdleTarget()) {
                    if (idle == null) return false;
                    Bitmap scene = workScenePlayer.Frame(new ScenePose(0, 0, 0, "think"), elapsed);
                    DrawWorkTransition(g, idle, scene, elapsed / (double)WorkSceneEntryDuration, true);
                }
            } else g.DrawImageUnscaled(workScenePlayer.Frame(WorkCycleTiming.At(WorkCycleElapsed(now)), elapsed), 0, 122);
            return true;
        }
        private static void DrawWorkLayer(Graphics g, Image image, double opacity, float dy)
        {
            opacity = Math.Max(0, Math.Min(1, opacity)); if (opacity <= 0) return;
            using (var attributes = new ImageAttributes()) {
                var matrix = new ColorMatrix(); matrix.Matrix33 = (float)opacity; attributes.SetColorMatrix(matrix);
                var corners = new[] { new PointF(0, 122 + dy), new PointF(272, 122 + dy), new PointF(0, 330 + dy) };
                g.DrawImage(image, corners, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, attributes);
            }
        }
        private static void DrawWorkTransition(Graphics g, Image before, Image after, double progress, bool entering)
        {
            double p = Math.Max(0, Math.Min(1, progress));
            // Cross the appearance behind a soft magical veil. Whole dragon
            // silhouettes retain their anatomy; no standing body is warped into
            // a companion dragon and no rectangular backing is painted.
            double oldAlpha = 1 - WalkSmooth(p / .60), newAlpha = WalkSmooth((p - .40) / .60);
            DrawWorkLayer(g, before, oldAlpha, entering ? 0 : (float)(-5 * p));
            DrawWorkLayer(g, after, newAlpha, entering ? (float)(6 * (1 - p)) : 0);
            double cloud = Math.Pow(Math.Sin(p * Math.PI), 1.5);
            if (cloud < .001) return;
            GraphicsState saved = g.Save(); g.SetClip(new RectangleF(0, 122, 272, 208));
            PointF[] centers = { new PointF(87, 202), new PointF(142, 225), new PointF(219, 192), new PointF(65, 288), new PointF(159, 286), new PointF(222, 278) };
            for (int i = 0; i < centers.Length; i++) {
                float radius = 39 + (float)cloud * 18, x = centers[i].X + (float)Math.Sin(p * 5 + i) * 5, y = centers[i].Y;
                using (var shape = new GraphicsPath()) {
                    shape.AddEllipse(x - radius, y - radius * .72F, radius * 2, radius * 1.44F);
                    using (var mist = new PathGradientBrush(shape)) {
                        mist.CenterPoint = new PointF(x, y); mist.CenterColor = Color.FromArgb((int)(230 * cloud), 239, 230, 250);
                        mist.SurroundColors = new[] { Color.FromArgb(0, 239, 230, 250) }; g.FillPath(mist, shape);
                    }
                }
            }
            for (int i = 0; i < 13; i++) {
                double angle = i * Math.PI * 2 / 13 + p * .45;
                float r = (float)(20 + p * 105), x = 136 + (float)Math.Cos(angle) * r, y = 235 + (float)Math.Sin(angle) * r * .68F;
                using (var star = new Pen(Color.FromArgb((int)(210 * cloud), i % 2 == 0 ? 144 : 244, i % 2 == 0 ? 112 : 238, i % 2 == 0 ? 193 : 255), 1.1F))
                    DrawSparkle(g, star, x, y, 1.4F + (float)cloud * 1.7F);
            }
            g.Restore(saved);
        }
        private string WorkSceneHint(long now)
        {
            long elapsed = Math.Max(0, now - workStarted);
            if (elapsed < WorkSceneEntryDuration) return "小龙来陪你工作啦～";
            return WorkCycleTiming.At(WorkCycleElapsed(now)).Stage == "type"
                ? "认真敲键盘，陪你把想法写出来～" : "托腮看看屏幕，想一想下一步～";
        }
        private void ReleaseWorkScene()
        {
            if (workScenePlayer != null) { workScenePlayer.Dispose(); workScenePlayer = null; }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private Bitmap informationCardCache;
        private string informationCardIdentity;
        private object cardWeather, cardHardware, cardQuota;
        private void DrawCachedInformationCard(Graphics g)
        {
            // Telemetry text changes much less often than the character. Keep
            // physical pixels at the current scale so cached text stays crisp.
            string identity = petName + "|" + activeHoliday + "|" + previewCondition + "|" + previewSeverity
                + "|" + displayMode + "|" + ActivityLabel + "|" + services.WeatherLocationVersion + "|" + sizePercent
                + "|" + DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute;
            if (informationCardCache == null || identity != informationCardIdentity
                || !Object.ReferenceEquals(cardWeather, weather) || !Object.ReferenceEquals(cardHardware, hardware) || !Object.ReferenceEquals(cardQuota, quota)) {
                if (informationCardCache != null) informationCardCache.Dispose();
                float scale = sizePercent / 100F;
                informationCardCache = new Bitmap(ScaledSize().Width, (int)Math.Ceiling(121 * scale), PixelFormat.Format32bppPArgb);
                using (Graphics cached = Graphics.FromImage(informationCardCache)) {
                    cached.Clear(Color.Transparent); cached.SmoothingMode = SmoothingMode.AntiAlias;
                    cached.PixelOffsetMode = PixelOffsetMode.HighQuality; cached.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    cached.ScaleTransform(scale, scale); DrawInformationCard(cached);
                }
                informationCardIdentity = identity; cardWeather = weather; cardHardware = hardware; cardQuota = quota;
            }
            GraphicsState state = g.Save(); g.ResetTransform(); g.DrawImageUnscaled(informationCardCache, 0, 0); g.Restore(state);
        }
        private void ReleaseRenderCaches()
        { if (informationCardCache != null) { informationCardCache.Dispose(); informationCardCache = null; } ReleaseFlightArtwork(); ReleaseHomeWalkArtwork(); ReleaseLeisureMotion(); ReleaseCharacterPlayer(); ReleaseFeedingFood(); ReleaseWorkScene(); ResetWorkTea(); }
        private void UpdateMotionCadence(long now)
        {
            // Short-lived interactions get a fine animation tick; idle keeps its
            // lighter cadence. Artwork no longer snaps between two slow poses.
            int interval = returnFlying || lifted || falling || homeWalking || WanderVisual || FeedingAt(now) || IsWorkVisual(now) || ClosingLaptopAt(now) || AttentionActive || !String.IsNullOrEmpty(leisure) || SippingAt(now) || LandingAt(now) ? 15 : 33;
            if (animationTimer.Interval != interval) animationTimer.Interval = interval;
        }
        private void DrawSuspendedAction(Graphics g, Image pet, long now)
        {
            RectangleF destination = new RectangleF(8, 122, 256, 208);
            if (falling) {
                // The held-skirt, both-legs-back pose stays coherent while the
                // window's subpixel gravity and a small continuous sway move it.
                float flutter = (float)Math.Sin((now - fallStarted) / 95.0) * .6F;
                g.DrawImage(pet, new RectangleF(destination.X - flutter / 2, destination.Y, destination.Width + flutter, destination.Height));
                return;
            }
            // Authored held poses move both legs, sleeves, wings and long hair
            // together. The old character's rectangular leg cuts are retired.
            g.DrawImage(pet, destination);
        }
        private static void DrawSwingLeg(Graphics g, Image pet, RectangleF source, float swing)
        {
            float x = 8 + source.X, y = 122 + source.Y;
            var corners = new PointF[] { new PointF(x, y), new PointF(x + source.Width, y), new PointF(x + swing, y + source.Height) };
            g.DrawImage(pet, corners, source, GraphicsUnit.Pixel);
        }
    }
}

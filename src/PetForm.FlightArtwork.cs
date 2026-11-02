using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private static readonly string[] FlightPhases = { "launch", "cruise", "approach", "land" };
        private static readonly int[] FlightCounts = { 32, 32, 40, 40 };
        private readonly Bitmap[] flightSheets = new Bitmap[4];
        private string flightSheetKey, flightOutfit;
        private long flightLandingStarted = -10000;
        private bool FlightFacingRight { get { return flightTo.X > flightFrom.X; } }
        private string FlightOutfitKey() { return CurrentLoopKey(); }
        private bool FlightLandingAt(long now)
        { return !returnFlying && !lifted && !falling && LandingAt(now) && now >= flightLandingStarted && now - flightLandingStarted < 650; }
        private void ReleaseFlightArtwork()
        {
            for (int i = 0; i < flightSheets.Length; i++) { if (flightSheets[i] != null) { flightSheets[i].Dispose(); flightSheets[i] = null; } }
            flightSheetKey = null;
        }
        private bool EnsureFlightArtwork(string outfit, bool right)
        {
            if (String.IsNullOrEmpty(outfit) || outfit.IndexOfAny(new char[] {'/', '\\', '.', ':'}) >= 0) return false;
            string direction = right ? "right" : "left", identity = outfit + "|" + direction;
            if (flightSheetKey == identity) return true;
            string root = Path.Combine(actionAssetsRoot, "..", "flight", outfit);
            for (int i = 0; i < 4; i++) if (!File.Exists(Path.Combine(root, FlightPhases[i] + "-" + direction + ".png"))) return false;
            ReleaseFlightArtwork();
            try {
                for (int i = 0; i < 4; i++) {
                    using (var source = new Bitmap(Path.Combine(root, FlightPhases[i] + "-" + direction + ".png"))) {
                        if (source.Width != 2176 || source.Height != 208 * ((FlightCounts[i] + 7) / 8)) throw new InvalidDataException("Invalid flight sprite dimensions.");
                        flightSheets[i] = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(flightSheets[i])) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                    }
                }
                flightSheetKey = identity; return true;
            } catch { ReleaseFlightArtwork(); return false; }
        }
        private bool PrepareFlightArtwork()
        {
            string outfit = FlightOutfitKey();
            if (!EnsureFlightArtwork(outfit, FlightFacingRight)) return false;
            flightOutfit = outfit; flightLandingStarted = -10000; return true;
        }
        private void FlightFrameAt(long now, out int phase, out int frame)
        {
            double elapsed = Math.Max(0, now - flightStarted);
            if (elapsed < 520) { phase = 0; frame = (int)Math.Floor(elapsed / 520 * (FlightCounts[0] - 1)); }
            else if (elapsed < flightDuration - 620) { phase = 1; frame = (int)Math.Floor((elapsed - 520) % 560 / 560 * FlightCounts[1]); }
            else { phase = 2; frame = (int)Math.Floor(Math.Max(0, elapsed - (flightDuration - 620)) / 620 * (FlightCounts[2] - 1)); }
            frame = Clamp(frame, 0, FlightCounts[phase] - 1);
        }
        private void DrawAuthoredFlightFrame(Graphics g, int phase, int frame)
        {
            if (!EnsureFlightArtwork(flightOutfit, FlightFacingRight)) return;
            var source = new Rectangle((frame % 8) * 272, (frame / 8) * 208, 272, 208);
            // Each frame is already a complete independently authored pose with
            // its own wings/tail. Only one transparent cell is painted per tick.
            g.DrawImage(flightSheets[phase], new Rectangle(0, 122, 272, 208), source, GraphicsUnit.Pixel);
        }
        private float FlightPitchAt(long now)
        {
            double elapsed = Math.Max(0, now - flightStarted); PointF point, tangent;
            FlightPathAt(elapsed / Math.Max(1, flightDuration), out point, out tangent);
            double angle = Math.Atan2(tangent.Y, Math.Max(.001, Math.Abs(tangent.X))) * 180 / Math.PI * (FlightFacingRight ? 1 : -1);
            double launch = Math.Max(0, Math.Min(1, (elapsed - 100) / 420)); launch = launch * launch * (3 - 2 * launch);
            double settle = Math.Max(0, Math.Min(1, (elapsed - (flightDuration - 620)) / 620)); settle = settle * settle * (3 - 2 * settle);
            return (float)(Math.Max(-65, Math.Min(65, angle)) * launch * (1 - settle));
        }
        private void DrawReturnFlight(Graphics g, Image fallback, long now)
        {
            GraphicsState state = g.Save(); float angle = FlightPitchAt(now);
            double radians = Math.Abs(angle) * Math.PI / 180, c = Math.Cos(radians), s = Math.Sin(radians);
            float fit = (float)Math.Min(1, Math.Min(268 / (272*c + 208*s), 204 / (272*s + 208*c)));
            g.TranslateTransform(136, 226); g.RotateTransform(angle); g.ScaleTransform(fit, fit); g.TranslateTransform(-136, -226);
            int phase, frame; FlightFrameAt(now, out phase, out frame);
            if (EnsureFlightArtwork(flightOutfit, FlightFacingRight)) DrawAuthoredFlightFrame(g, phase, frame);
            else if (fallback != null) g.DrawImage(fallback, new Rectangle(8, 122, 256, 208));
            g.Restore(state);
        }
        private void DrawFlightLanding(Graphics g, long now)
        {
            int frame = Clamp((int)((now - flightLandingStarted) / 650F * (FlightCounts[3] - 1)), 0, FlightCounts[3] - 1);
            DrawAuthoredFlightFrame(g, 3, frame);
        }
    }
}

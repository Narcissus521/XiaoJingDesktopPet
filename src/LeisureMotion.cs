using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace XiaoJingDesktopPet
{
    internal sealed class LeisureCatalog
    {
        public int revision;
        public Dictionary<string, LeisureProfile> outfits;
        public Dictionary<string, int[]> idleFaces;
    }
    internal sealed class LeisureProfile
    {
        public int bubblePose, removedBubbleComponents;
        public int[] bubbleEmitter, book, cup, faceBubbles, faceBook, faceTea, rainPalm, feedHand, feedMouth;
        internal int[] Face(string action) { return action == "bubbles" ? faceBubbles : action == "book" ? faceBook : faceTea; }
    }
    internal sealed class LeisureMotion : IDisposable
    {
        internal readonly string Identity, Action;
        internal readonly LeisureProfile Profile;
        private readonly Bitmap source;
        private readonly Bitmap output = new Bitmap(256, 208, PixelFormat.Format32bppPArgb);

        internal LeisureMotion(string id, string action, Image image, LeisureProfile profile, Image unusedCup = null)
        {
            Identity = id + "|" + action; Action = action; Profile = profile;
            source = new Bitmap(image);
            if (action == "rain" && (profile.rainPalm == null || profile.rainPalm.Length != 2))
                throw new ArgumentException("Missing calibrated dragon palm.");
        }
        internal Bitmap Frame(long elapsed, double puttingAway)
        {
            elapsed = Math.Max(0, elapsed); puttingAway = Math.Max(0, Math.Min(1, puttingAway));
            using (Graphics g = Graphics.FromImage(output)) {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                // Keep the connected hand/tool/dress illustration intact. Tea,
                // fishing and bubbles use their separately authored full cycles.
                float breathe = (float)Math.Sin(elapsed / 850.0) * .35F;
                g.TranslateTransform(0, breathe); g.DrawImageUnscaled(source, 0, 0);
                if (Action == "rain") DrawRain(g, elapsed, 1 - puttingAway);
                if (Action == "book" && puttingAway < .8) DrawPage(g, elapsed);
            }
            return output;
        }
        private void DrawRain(Graphics g, long elapsed, double visibility)
        {
            float handX = Profile.rainPalm[0], handY = Profile.rainPalm[1];
            using (var trail = new Pen(Color.FromArgb((int)(190 * visibility), 99, 177, 221), 1.3F))
            using (var drop = new SolidBrush(Color.FromArgb((int)(240 * visibility), 174, 230, 252))) {
                for (int i = 0; i < 4; i++) {
                    double age = ((elapsed + i * 337) % 1348) / 1000.0;
                    float landing = handX + (i - 1.5F) * 2.7F;
                    if (age < .56) {
                        float remaining = (float)(1 - Math.Pow(age / .56, 1.6));
                        float x = landing + 7 * remaining, y = handY - (48 + i * 3) * remaining;
                        g.DrawLine(trail, x + 1, y - 6, x, y); g.FillEllipse(drop, x - 1, y - 2, 2, 3);
                    } else if (age < .78) {
                        float t = (float)((age - .56) / .22);
                        using (var splash = new SolidBrush(Color.FromArgb((int)(215 * visibility * (1 - t)), 126, 202, 240)))
                            for (int ray = -1; ray <= 1; ray++) {
                                float x = landing + ray * (1 + 9 * t), y = handY - (float)Math.Sin(t * Math.PI) * (4 + Math.Abs(ray) * 2);
                                g.FillEllipse(splash, x - 1, y - 1, 2, 2);
                            }
                    }
                }
            }
        }
        private void DrawPage(Graphics g, long elapsed)
        {
            if (Profile.book == null || Profile.book.Length != 4) return;
            double phase = elapsed % 3600 / 3600.0;
            if (phase < .52 || phase > .87) return;
            float t = (float)((phase - .52) / .35); int[] b = Profile.book;
            GraphicsState saved = g.Save();
            g.SetClip(new RectangleF(b[0] + 2, b[1], b[2] - 4, b[3]));
            float middle = b[0] + b[2] * .5F, y = b[1] + b[3] * .25F;
            using (var page = new Pen(Color.FromArgb((int)(220 * Math.Sin(t * Math.PI)), 234, 226, 244), 1))
                g.DrawBezier(page, middle - b[2] * .35F * (1 - t), y + 3, middle - 6, y - 6 * (float)Math.Sin(t * Math.PI), middle + 6, y - 6 * (float)Math.Sin(t * Math.PI), middle + b[2] * .35F * t, y + 3);
            g.Restore(saved);
        }
        public void Dispose() { source.Dispose(); output.Dispose(); }
    }
}

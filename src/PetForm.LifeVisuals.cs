using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private void DrawLifeWorld(Graphics g, long now)
        {
            DrawFallAirflow(g, now);
            if (lifted || falling)
            {
                using (Brush shadow = new SolidBrush(AlphaColor(lifted ? 55 : 80, 36, 52, 73))) g.FillEllipse(shadow, 96, 319, 72, 10);
            }
        }
        private void DrawInteractionEffects(Graphics g, long now)
        {
            if (PettingAt(now))
            {
                double t = (now - petStarted) / 2200.0;
                using (Brush heart = new SolidBrush(AlphaColor((int)(230 * Math.Sin(Math.PI * t)), 255, 151, 178)))
                using (Font font = UiFont(patStreak >= 3 ? 15 : 12, FontStyle.Bold))
                    for (int i = 0; i < (patStreak >= 3 ? 5 : 3); i++) g.DrawString("♥", font, heart, 64 + i * 34, 152 - (float)t * 18 - (i % 2) * 9);
            }
            if (lifted && now - rapidDragAt < 1000)
            { using (Font f = UiFont(17, FontStyle.Bold)) using (Brush b = new SolidBrush(Color.FromArgb(245, 189, 85))) g.DrawString("!", f, b, 205, 157); }
            if (LandingAt(now))
            {
                float t = (now - landStarted) / 1000F;
                if (t < .65F) for (int i = 0; i < 7; i++)
                {
                    float x = 136 + (i - 3) * (8 + t * 45);
                    using (Brush dust = new SolidBrush(AlphaColor((int)(140 * (1 - t / .65F)), 205, 224, 238))) g.FillEllipse(dust, x, 318 - i % 2 * 4, 6 + t * 8, 4);
                }
            }
            // Page motion belongs inside the book's clipped local artwork.
            // The former opaque screen-space quadrilateral produced a white box.
            if (leisure == "nap") using (Font font = UiFont(11, FontStyle.Bold)) using (Brush z = new SolidBrush(AlphaColor(150, 144, 189, 233))) g.DrawString("z Z", font, z, 195, 154 - (float)(now % 2000) / 180);
            if (leisure == "leaf") DrawWindParticle(g, 48 + (float)((now - leisureStarted) / 70.0 % 160), 278 + (float)Math.Sin(now / 400.0) * 12, 6, (float)(now / 13.0), false, 220);
            if (leisure == "festival")
            {
                using (Brush glow = new SolidBrush(AlphaColor(160, WorkingCuffColor().R, WorkingCuffColor().G, WorkingCuffColor().B))) for (int i = 0; i < 5; i++) g.FillEllipse(glow, 50 + i * 39, 168 + (float)Math.Sin(now / 250.0 + i) * 9, 5, 5);
                Image food; if (activeHoliday != null && images.TryGetValue("food/" + activeHoliday, out food) && (now - leisureStarted) % 6000 >= 1800 && (now - leisureStarted) % 6000 < 4000) g.DrawImage(food, new RectangleF(123, 245, 33, 30));
            }
            if (FishingAt(now))
            {
                PointF p = FloatPoint(); bool bite = now >= biteAt && now - biteAt < 3400;
                float bob = bite ? (float)Math.Sin(now / 55.0) * 5 : (float)Math.Sin(now / 430.0) * 1.5F;
                using (Pen ring = new Pen(AlphaColor(bite ? 220 : 90, 125, 203, 245), bite ? 2 : 1)) g.DrawEllipse(ring, p.X - 13, p.Y + 3, 26, 7);
                if (bite) using (Font font = UiFont(12, FontStyle.Bold)) using (Brush signal = new SolidBrush(Color.FromArgb(249, 190, 69))) g.DrawString("!", font, signal, p.X + 7, p.Y - 32 + bob);
            }
            if (now - caughtAt >= 0 && now - caughtAt < 2600)
            {
                Image fish; if (images.TryGetValue("life/fish-" + caughtType, out fish)) g.DrawImage(fish, new RectangleF(195, 236 - Math.Min(55, (now - caughtAt) / 22F), 49, 42));
            }
        }
        private void ApplyLaptopTransition(Graphics g, long now)
        {
            if (ClosingLaptopAt(now))
            { float p = (now - closeLaptopStarted) / 650F; g.TranslateTransform(0, 300); g.ScaleTransform(1, Math.Max(.06F, 1 - p)); g.TranslateTransform(0, -300); }
            else if (activityBusy == true && now - workStarted >= 0 && now - workStarted < 500)
                g.TranslateTransform(0, 15 * (1 - (now - workStarted) / 500F));
        }
        private bool DrawTypingAt(long now) { return !UsingActionArtwork(now) && !ClosingLaptopAt(now) && !SippingAt(now); }
        private bool TryCurrentFoodImage(out Image image)
        {
            if (activeHoliday != null) return images.TryGetValue("food/" + activeHoliday, out image);
            if (CurrentCondition() == "sun" && CurrentSeverity() == 3) return images.TryGetValue("life/icepop", out image);
            image = null; return false;
        }
    }

    internal sealed class FishCollectionForm : Form
    {
        private readonly Dictionary<string, Image> images;
        private readonly int[] counts;
        private readonly string[] names;
        internal FishCollectionForm(Dictionary<string, Image> images, int[] counts, string[] names)
        {
            this.images = images; this.counts = (int[])counts.Clone(); this.names = names;
            Text = "我的鱼缸收藏"; ClientSize = new Size(540, 350); FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; DoubleBuffered = true;
            BackColor = Color.FromArgb(238, 245, 252);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Font title = new Font("Microsoft YaHei UI", 12, FontStyle.Bold)) g.DrawString("浮漂抖动时点击，就能收集新朋友", title, Brushes.SteelBlue, 18, 15);
            using (Font font = new Font("Microsoft YaHei UI", 10))
            using (Font mystery = new Font("Microsoft YaHei UI", 25)) for (int i = 0; i < 6; i++)
            {
                int x = 16 + i % 3 * 175, y = 65 + i / 3 * 135; Image fish;
                if (counts[i] > 0 && images.TryGetValue("life/fish-" + i, out fish)) g.DrawImage(fish, new Rectangle(x + 18, y, 128, 98));
                else g.DrawString("?", mystery, Brushes.LightSteelBlue, x + 57, y + 20);
                g.DrawString(counts[i] > 0 ? names[i] + "  ×" + counts[i] : "尚未遇见", font, Brushes.DimGray, x + 27, y + 100);
            }
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private float FallAirStrength(long now)
        {
            if (lifted) return 0;
            float speed = (float)Math.Min(1, Math.Abs(fallVelocity) / (950 * sizePercent / 100.0));
            if (falling) return (.18F + .82F * speed) * Math.Min(1, Math.Max(0, (now - fallStarted) / 100F));
            long age = now - landStarted;
            return age >= 0 && age < 160 ? (.18F + .82F * speed) * (1 - age / 160F) : 0;
        }
        private void DrawFallAirflow(Graphics g, long now)
        {
            float strength = FallAirStrength(now); if (strength <= 0) return;
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(1, 125, 270, 204), CombineMode.Intersect);
            for (int side = 0; side < 2; side++) for (int i = 0; i < 3; i++) {
                double phase = (now / (470.0 - i * 65) + i * .31 + side * .17) % 1;
                float y = 306 - (float)phase * 145, length = 19 + strength * 35 + i * 3;
                float x = side == 0 ? 10 + i * 10 : 262 - i * 10;
                float curve = side == 0 ? -4 : 4;
                int alpha = (int)(100 * strength * Math.Sin(phase * Math.PI));
                using (var path = new GraphicsPath())
                using (var pen = new Pen(AlphaColor(alpha, 216, 241, 253), .9F + strength * .45F)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    path.AddBezier(x, y, x + curve, y - length * .3F, x + curve, y - length * .7F, x + curve * .2F, y - length);
                    g.DrawPath(pen, path);
                }
            }
            g.Restore(state);
        }
    }
}

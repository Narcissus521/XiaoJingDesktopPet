using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace XiaoJingDesktopPet
{
    // Every frame contains this character's own arms, sleeves, dress, legs,
    // wings and tail. No limb masks from a different character are reused.
    internal sealed class WalkingRig : IDisposable
    {
        private readonly Bitmap[] sheets = new Bitmap[2];
        private const int FrameCount = 128;

        internal WalkingRig(string root)
        {
            try {
                for (int direction = 0; direction < 2; direction++) {
                    string name = direction == 0 ? "walk-left.png" : "walk-right.png";
                    using (var source = new Bitmap(Path.Combine(root, name))) {
                        if (source.Width != 2176 || source.Height != 3328)
                            throw new InvalidDataException("Invalid authored walking atlas.");
                        sheets[direction] = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(sheets[direction])) {
                            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            g.DrawImageUnscaled(source, 0, 0);
                        }
                    }
                }
            } catch { Dispose(); throw; }
        }

        internal void Draw(Graphics g, double phase, double stride, double amount, bool right)
        {
            double cycle = phase - Math.Floor(phase);
            int frame = amount <= 0 ? 0 : Math.Min(FrameCount - 1, (int)(cycle * FrameCount));
            g.DrawImage(sheets[right ? 1 : 0], new Rectangle(0, 122, 272, 208),
                new Rectangle(frame % 8 * 272, frame / 8 * 208, 272, 208), GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            foreach (Bitmap sheet in sheets) if (sheet != null) sheet.Dispose();
        }
    }
}

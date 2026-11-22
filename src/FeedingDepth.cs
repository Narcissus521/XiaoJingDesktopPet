using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;

namespace XiaoJingDesktopPet
{
    // One foreground-hand silhouette and one support point for each authored
    // contact frame. Both use native actor pixels, before window scaling.
    internal sealed class FeedingDepth
    {
        private const int Count = 97, Side = 32;
        private readonly Point[] origins = new Point[Count];
        private readonly PointF[] supports = new PointF[Count];
        private readonly byte[][] hands = new byte[Count][];
        private int first, next;
        private double fraction;

        internal FeedingDepth(string path)
        {
            using (var file = File.OpenRead(path))
            using (var header = new BinaryReader(file))
            {
                if (header.ReadInt32() != 0x31504446) throw new InvalidDataException("Unknown feeding depth format.");
                using (var stream = new DeflateStream(file, CompressionMode.Decompress))
                using (var reader = new BinaryReader(stream))
                {
                    for (int i = 0; i < Count; ++i)
                    {
                        origins[i] = new Point(reader.ReadByte(), reader.ReadByte());
                        supports[i] = new PointF(reader.ReadSingle(), reader.ReadSingle());
                        hands[i] = reader.ReadBytes(Side * Side);
                        if (hands[i].Length != Side * Side) throw new EndOfStreamException();
                    }
                }
            }
        }

        internal void Sample(long elapsed)
        {
            double at = elapsed < FeedingCycle.CarryStart
                ? 32 * FeedingCycle.CloseProgress(elapsed)
                : 32 + 64 * FeedingCycle.CarryProgress(elapsed);
            first = (int)at;
            next = Math.Min(Count - 1, first + 1);
            fraction = at - first;
        }

        internal PointF Support
        {
            get { return new PointF((float)(supports[first].X + (supports[next].X - supports[first].X) * fraction),
                                    (float)(supports[first].Y + (supports[next].Y - supports[first].Y) * fraction)); }
        }

        private double Coverage(int frame, int x, int y)
        {
            x -= origins[frame].X; y -= origins[frame].Y;
            return x >= 0 && x < Side && y >= 0 && y < Side ? hands[frame][y * Side + x] / 255.0 : 0;
        }

        internal double Coverage(int x, int y)
        {
            double a = Coverage(first, x, y);
            return fraction == 0 ? a : a + (Coverage(next, x, y) - a) * fraction;
        }
    }
}

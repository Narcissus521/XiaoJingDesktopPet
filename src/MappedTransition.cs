using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace XiaoJingDesktopPet
{
    internal sealed class MappedTransition : IDisposable
    {
        private readonly SparseSceneBlend map;
        private readonly int[] pixels = new int[272 * 208];
        private int[] target;
        private readonly Bitmap output = new Bitmap(272, 208, PixelFormat.Format32bppPArgb);
        internal MappedTransition(string path) { map = new SparseSceneBlend(path); }
        internal void SetTarget(Bitmap image) { target = ScenePixels.Read(image); }
        internal Bitmap Frame(Bitmap source, double progress)
        {
            if (target == null) throw new InvalidOperationException("Missing attention target.");
            map.Blend(ScenePixels.Read(source), target, progress, pixels);
            ScenePixels.Write(output, pixels); return output;
        }
        public void Dispose() { output.Dispose(); target = null; }
    }
}

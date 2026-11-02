using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace XiaoJingDesktopPet
{
    internal sealed class MotionInfluence
    {
        internal readonly int[] Indices;
        internal readonly float[] Weights, X, Y;
        internal MotionInfluence(int width,int height,float cx,float cy,float rx,float ry,float px,float py,float minY,float minX=0,float maxX=10000)
        {
            var ids=new List<int>();var weights=new List<float>();var xs=new List<float>();var ys=new List<float>();
            for(int y=Math.Max(0,(int)(cy-ry));y<Math.Min(height,(int)(cy+ry+1));y++)for(int x=Math.Max(0,(int)(cx-rx));x<Math.Min(width,(int)(cx+rx+1));x++){
                float w=Smooth(1-(x-cx)*(x-cx)/(rx*rx)-(y-cy)*(y-cy)/(ry*ry))*Smooth((y-minY)/14F)*Smooth((x-minX)/6F)*Smooth((maxX-x)/6F);
                if(w<=0)continue;ids.Add(y*width+x);weights.Add(w);xs.Add(x-px);ys.Add(y-py);
            }
            Indices=ids.ToArray();Weights=weights.ToArray();X=xs.ToArray();Y=ys.ToArray();
        }
        private static float Smooth(float n){n=Math.Max(0,Math.Min(1,n));return n*n*(3-2*n);}
    }
    internal sealed class MotionSurface : IDisposable
    {
        internal readonly int Width,Height;
        internal readonly Bitmap Output;
        private readonly int[] source,target;
        private readonly float[] dx,dy;
        private readonly bool[] affected;
        private readonly List<int> changed=new List<int>();
        internal MotionSurface(Image image)
        {
            Width=image.Width;Height=image.Height;Output=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
            source=new int[Width*Height];target=new int[source.Length];dx=new float[source.Length];dy=new float[source.Length];affected=new bool[source.Length];
            using(var b=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb)){
                using(Graphics g=Graphics.FromImage(b)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);}
                BitmapData data=b.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try{Marshal.Copy(data.Scan0,source,0,source.Length);}finally{b.UnlockBits(data);}
            }
        }
        internal void Begin(){foreach(int i in changed){dx[i]=dy[i]=0;affected[i]=false;}changed.Clear();Array.Copy(source,target,source.Length);}
        internal void Add(MotionInfluence field,float inverseAngle,float moveX,float moveY)
        {
            for(int j=0;j<field.Indices.Length;j++){
                int i=field.Indices[j];float w=field.Weights[j],a=inverseAngle*w,a2=a*a;
                float c=1-a2*.5F+a2*a2/24F,s=a-a*a2/6F;
                dx[i]+=(c-1)*field.X[j]-s*field.Y[j]-moveX*w;
                dy[i]+=s*field.X[j]+(c-1)*field.Y[j]-moveY*w;
                if(!affected[i]){affected[i]=true;changed.Add(i);}
            }
        }
        internal Bitmap Finish()
        {
            foreach(int i in changed)target[i]=Sample(i%Width+dx[i],i/Width+dy[i]);
            BitmapData data=Output.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(target,0,data.Scan0,target.Length);}finally{Output.UnlockBits(data);}return Output;
        }
        private int Pixel(int x,int y){return x<0||x>=Width||y<0||y>=Height?0:source[y*Width+x];}
        private int Sample(float x,float y)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),fx=(int)((x-ix)*256),fy=(int)((y-iy)*256),a=Pixel(ix,iy),b=Pixel(ix+1,iy),c=Pixel(ix,iy+1),d=Pixel(ix+1,iy+1),result=0;
            for(int shift=0;shift<=24;shift+=8){int top=((a>>shift)&255)*(256-fx)+((b>>shift)&255)*fx,bottom=((c>>shift)&255)*(256-fx)+((d>>shift)&255)*fx;result|=((top*(256-fy)+bottom*fy+32768)>>16)<<shift;}return result;
        }
        public void Dispose(){Output.Dispose();}
    }
}

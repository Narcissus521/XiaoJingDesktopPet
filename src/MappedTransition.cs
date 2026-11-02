using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
namespace XiaoJingDesktopPet
{
    // Displacement maps are baked offline from the existing outfit artwork.
    // Runtime needs only GDI+ and bilinear sampling, with no vision dependency.
    internal sealed class MappedTransition : IDisposable
    {
        private const int Width=272,Height=208,Pixels=Width*Height;
        private float[] maps;
        private readonly int frames;
        private readonly int[] source=new int[Pixels],target=new int[Pixels],output=new int[Pixels];
        private readonly Bitmap bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        internal MappedTransition(string path)
        {
            try{
                byte[] bytes;using(var file=File.OpenRead(path))using(var gzip=new GZipStream(file,CompressionMode.Decompress))using(var memory=new MemoryStream()){gzip.CopyTo(memory);bytes=memory.ToArray();}
                using(var reader=new BinaryReader(new MemoryStream(bytes))){
                    if(reader.ReadInt32()!=0x3131334D||reader.ReadInt32()!=Width||reader.ReadInt32()!=Height)throw new InvalidDataException("Invalid attention map header.");
                    frames=reader.ReadInt32();int gw=reader.ReadInt32(),gh=reader.ReadInt32();if(frames!=17||gw!=69||gh!=53||bytes.Length!=24+frames*4*gw*gh*2)throw new InvalidDataException("Invalid attention map dimensions.");
                    var grid=new short[frames*4*gw*gh];Buffer.BlockCopy(bytes,24,grid,0,bytes.Length-24);maps=new float[frames*4*Pixels];
                    for(int f=0;f<frames;f++)for(int channel=0;channel<4;channel++){
                        int offset=(f*4+channel)*Pixels,start=(f*4+channel)*gw*gh;
                        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){
                            int a=start+(y/4)*gw+x/4;float fx=x%4*.25F,fy=y%4*.25F;
                            float top=grid[a]+(grid[a+1]-grid[a])*fx,bottom=grid[a+gw]+(grid[a+gw+1]-grid[a+gw])*fx;
                            maps[offset+y*Width+x]=(top+(bottom-top)*fy)/16F;
                        }
                    }
                }
            }catch{Dispose();throw;}
        }
        internal void SetTarget(Bitmap image){Read(image,target);}
        internal Bitmap Frame(Bitmap image,double progress)
        {
            progress=Math.Max(0,Math.Min(1,progress));Read(image,source);
            if(progress<=0)Array.Copy(source,output,Pixels);
            else if(progress>=1)Array.Copy(target,output,Pixels);
            else{
                double f=progress*(frames-1);int first=Math.Min(frames-2,(int)f);float fraction=(float)(f-first);
                double blend=Math.Max(0,Math.Min(1,(progress-.4)/.2));blend=blend*blend*(3-2*blend);int weight=(int)Math.Round(blend*256);
                int offset=first*4*Pixels,next=offset+4*Pixels;
                for(int i=0;i<Pixels;i++){
                    float ax=Map(offset+i,next+i,fraction),ay=Map(offset+Pixels+i,next+Pixels+i,fraction),bx=Map(offset+2*Pixels+i,next+2*Pixels+i,fraction),by=Map(offset+3*Pixels+i,next+3*Pixels+i,fraction);
                    int a=Sample(source,ax,ay),b=Sample(target,bx,by),value=0;
                    for(int shift=0;shift<=24;shift+=8)value|=(((((a>>shift)&255)*(256-weight)+((b>>shift)&255)*weight)+128)>>8)<<shift;
                    output[i]=value;
                }
            }
            var data=bitmap.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(output,0,data.Scan0,Pixels);}finally{bitmap.UnlockBits(data);}return bitmap;
        }
        private float Map(int a,int b,float t){return maps[a]+(maps[b]-maps[a])*t;}
        private static int Pixel(int[] p,int x,int y){return x<0||x>=Width||y<0||y>=Height?0:p[y*Width+x];}
        private static int Sample(int[] p,float x,float y)
        {
            int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),fx=(int)((x-ix)*256),fy=(int)((y-iy)*256),a=Pixel(p,ix,iy),b=Pixel(p,ix+1,iy),c=Pixel(p,ix,iy+1),d=Pixel(p,ix+1,iy+1),result=0;
            for(int shift=0;shift<=24;shift+=8){int top=((a>>shift)&255)*(256-fx)+((b>>shift)&255)*fx,bottom=((c>>shift)&255)*(256-fx)+((d>>shift)&255)*fx;result|=((top*(256-fy)+bottom*fy+32768)>>16)<<shift;}return result;
        }
        private static void Read(Bitmap image,int[] p){var data=image.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{Marshal.Copy(data.Scan0,p,0,Pixels);}finally{image.UnlockBits(data);}}
        public void Dispose(){maps=null;bitmap.Dispose();}
    }
}

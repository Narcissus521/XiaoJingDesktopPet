using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
namespace XiaoJingDesktopPet {
 internal sealed class SparseSceneBlend {
  internal readonly int Width,Height;
  private readonly int frameCount,gridWidth,gridHeight,gridSize;
  private readonly short[] maps;
  private readonly float[] nodes;
  internal SparseSceneBlend(string path){
   byte[] bytes;using(var file=File.OpenRead(path))using(var gzip=new GZipStream(file,CompressionMode.Decompress))using(var memory=new MemoryStream()){gzip.CopyTo(memory);bytes=memory.ToArray();}
   using(var reader=new BinaryReader(new MemoryStream(bytes))){if(reader.ReadInt32()!=0x3231334D)throw new InvalidDataException("Invalid scene flow map.");Width=reader.ReadInt32();Height=reader.ReadInt32();frameCount=reader.ReadInt32();gridWidth=reader.ReadInt32();gridHeight=reader.ReadInt32();}
   if(Width<100||Width>512||Height<100||Height>400||Width%4!=0||Height%4!=0||frameCount!=17||gridWidth!=Width/4+1||gridHeight!=Height/4+1)throw new InvalidDataException("Invalid scene map dimensions.");
   gridSize=gridWidth*gridHeight;if(bytes.Length!=24+frameCount*4*gridSize*2)throw new InvalidDataException("Incomplete scene map.");maps=new short[frameCount*4*gridSize];Buffer.BlockCopy(bytes,24,maps,0,bytes.Length-24);nodes=new float[4*gridSize];
  }
  internal void Blend(int[] source,int[] target,double progress,int[] output){
   int pixels=Width*Height;if(source.Length!=pixels||target.Length!=pixels||output.Length!=pixels)throw new ArgumentException("Scene pixel size mismatch.");
   progress=Math.Max(0,Math.Min(1,progress));if(progress<=0){Array.Copy(source,output,pixels);return;}if(progress>=1){Array.Copy(target,output,pixels);return;}
   double f=progress*(frameCount-1);int first=Math.Min(frameCount-2,(int)f);float t=(float)(f-first);int start=first*4*gridSize,next=start+4*gridSize;
   for(int i=0;i<nodes.Length;i++)nodes[i]=(maps[start+i]+(maps[next+i]-maps[start+i])*t)/16F;
   // The geometry moves continuously; a single texture avoids doubled eyes, cups and rods.
   int weight=progress<.5?0:256;
   for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){
    int node=(y/4)*gridWidth+x/4,index=y*Width+x;float fx=(x%4)*.25F,fy=(y%4)*.25F;int a=0,b=0;
    if(weight<256)a=Sample(source,At(node,fx,fy),At(node+gridSize,fx,fy));
    if(weight>0)b=Sample(target,At(node+gridSize*2,fx,fy),At(node+gridSize*3,fx,fy));
    if(weight==0)output[index]=a;else if(weight==256)output[index]=b;else{int c=0;for(int s=0;s<=24;s+=8)c|=((((a>>s)&255)*(256-weight)+((b>>s)&255)*weight+128)>>8)<<s;output[index]=c;}
   }
  }
  private float At(int i,float x,float y){float a=nodes[i]+(nodes[i+1]-nodes[i])*x,b=nodes[i+gridWidth]+(nodes[i+gridWidth+1]-nodes[i+gridWidth])*x;return a+(b-a)*y;}
  private int Pixel(int[] p,int x,int y){return x<0||x>=Width||y<0||y>=Height?0:p[y*Width+x];}
  private int Sample(int[] p,float x,float y){int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y),fx=(int)((x-ix)*256),fy=(int)((y-iy)*256),a=Pixel(p,ix,iy),b=Pixel(p,ix+1,iy),c=Pixel(p,ix,iy+1),d=Pixel(p,ix+1,iy+1),v=0;for(int s=0;s<=24;s+=8){int top=((a>>s)&255)*(256-fx)+((b>>s)&255)*fx,bot=((c>>s)&255)*(256-fx)+((d>>s)&255)*fx;v|=((top*(256-fy)+bot*fy+32768)>>16)<<s;}return v;}
 }
 internal static class ScenePixels {
  internal static int[] Read(Image image){var direct=image as Bitmap;if(direct!=null&&direct.PixelFormat==PixelFormat.Format32bppPArgb){var locked=direct.LockBits(new Rectangle(0,0,direct.Width,direct.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{var p=new int[direct.Width*direct.Height];Marshal.Copy(locked.Scan0,p,0,p.Length);return p;}finally{direct.UnlockBits(locked);}}using(var b=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppPArgb)){using(var g=Graphics.FromImage(b)){g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(image,0,0);}var data=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);try{var pixels=new int[b.Width*b.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);return pixels;}finally{b.UnlockBits(data);}}}
  internal static void Write(Bitmap image,int[] pixels){var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);try{Marshal.Copy(pixels,0,data.Scan0,pixels.Length);}finally{image.UnlockBits(data);}}
 }
}

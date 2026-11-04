using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet {
 internal sealed class SceneProfile {
  public double[][] ring {get;set;} public double[] floatPoint {get;set;}
 }
 internal sealed class SceneActionPlayer : IDisposable {
  internal readonly string Identity,Kind,Outfit;
  internal readonly SceneProfile Profile;
  private readonly string folder;
  private readonly int[][] poses=new int[12][];
  private readonly Dictionary<string,SparseSceneBlend> maps=new Dictionary<string,SparseSceneBlend>();
  private readonly Bitmap output=new Bitmap(272,208,PixelFormat.Format32bppPArgb);
  private readonly int[] pixels=new int[272*208];
  internal SceneActionPlayer(string root,string kind,string outfit){
   Kind=kind;Outfit=outfit;Identity=outfit+"|"+kind;folder=Path.Combine(root,kind,outfit);
   Profile=new JavaScriptSerializer().Deserialize<SceneProfile>(File.ReadAllText(Path.Combine(folder,"profile.json")));
   for(int i=0;i<12;i++)using(var b=new Bitmap(Path.Combine(folder,"pose-"+i.ToString("00")+".png")))poses[i]=ScenePixels.Read(b);
  }
  internal SparseSceneBlend ExitMap(string loop,int pose){return Map(Path.Combine("exit",loop,pose.ToString("00")+".map.gz"));}
  private SparseSceneBlend Map(string name){SparseSceneBlend map;if(!maps.TryGetValue(name,out map)){map=new SparseSceneBlend(Path.Combine(folder,name));maps.Add(name,map);}return map;}
  private static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
  internal Bitmap Frame(ScenePose pose,long elapsed){
   if(pose.From==pose.To)Array.Copy(poses[pose.From],pixels,pixels.Length);
   else Map(pose.From.ToString("00")+"-"+pose.To.ToString("00")+".map.gz").Blend(poses[pose.From],poses[pose.To],Smooth(pose.Progress),pixels);
   ScenePixels.Write(output,pixels);
   if(Kind=="bubbles")DrawBubbles(elapsed);
   return output;
  }
  internal Bitmap Entry(Image idle,string loop,double progress){ExitMap(loop,0).Blend(poses[0],ScenePixels.Read(idle),1-Smooth(progress),pixels);ScenePixels.Write(output,pixels);return output;}
  private PointF RingAt(long time){
   ScenePose p=BubbleCycleTiming.At(time);double t=Smooth(p.Progress);
   if(Profile.ring==null||Profile.ring.Length!=12)return new PointF(104,117);
   return new PointF((float)(Profile.ring[p.From][0]*(1-t)+Profile.ring[p.To][0]*t),(float)(Profile.ring[p.From][1]*(1-t)+Profile.ring[p.To][1]*t));
  }
  private void DrawBubbles(long elapsed){
   using(var g=Graphics.FromImage(output)){
    g.SmoothingMode=SmoothingMode.AntiAlias;
    long cycle=Math.Max(0,elapsed)/BubbleCycleTiming.Period;
    for(long c=Math.Max(0,cycle-1);c<=cycle;c++)for(int window=0;window<2;window++){
     int start=window==0?3550:6100,end=window==0?5450:7100;
     for(int born=start;born<end;born+=BubbleCycleTiming.EmissionInterval){
      long age=elapsed-(c*BubbleCycleTiming.Period+born);if(age<0||age>=BubbleCycleTiming.ParticleLife)continue;
      double p=age/(double)BubbleCycleTiming.ParticleLife;PointF at=RingAt(born);int seed=born/10;
      float radius=(float)((3.5+(seed%3)*1.4)*Math.Min(1,age/160.0));
      float x=at.X-(float)(p*(58+seed%4*5))+ (float)Math.Sin(p*5+seed)*2,y=at.Y-(float)(p*(50+seed%3*8));
      int alpha=(int)(230*Math.Min(1,age/160.0)*Math.Min(1,(BubbleCycleTiming.ParticleLife-age)/300.0));
      if(radius<1.3F||alpha<2)continue;
      using(var fill=new SolidBrush(Color.FromArgb(alpha/4,100,213,245)))using(var edge=new Pen(Color.FromArgb(alpha,44,145,193),1.2F))using(var light=new Pen(Color.FromArgb(alpha,255,255,255),1.25F)){
       g.FillEllipse(fill,x-radius,y-radius,radius*2,radius*2);g.DrawEllipse(edge,x-radius,y-radius,radius*2,radius*2);g.DrawArc(light,x-radius+1,y-radius+1,radius*2-2,radius*2-2,190,80);
      }
     }
    }
   }
  }
  public void Dispose(){output.Dispose();maps.Clear();}
 }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet {
 internal sealed class SceneProfile {
  public double[][] ring {get;set;} public double[][] rodTip {get;set;} public double[] floatPoint {get;set;}
 }
 internal sealed class SceneActionPlayer : IDisposable {
  internal readonly string Identity,Kind,Outfit;
  internal readonly SceneProfile Profile;
  private readonly string folder;
  private readonly int[][] poses=new int[12][];
  private readonly Dictionary<string,SparseSceneBlend> maps=new Dictionary<string,SparseSceneBlend>();
  private readonly Bitmap output=new Bitmap(272,208,PixelFormat.Format32bppPArgb);
  private readonly int[] pixels=new int[272*208];
  private Bitmap fishingSurface;
  private BakedWorkMotion workMotion;
  private readonly PointF[] teaCups;
  internal SceneActionPlayer(string root,string kind,string outfit){
   Kind=kind;Outfit=outfit;Identity=outfit+"|"+kind;folder=Path.Combine(root,kind,outfit);
   Profile=new JavaScriptSerializer().Deserialize<SceneProfile>(File.ReadAllText(Path.Combine(folder,"profile.json")));
   for(int i=0;i<12;i++)using(var b=new Bitmap(Path.Combine(folder,"pose-"+i.ToString("00")+".png")))poses[i]=ScenePixels.Read(b);
   if(kind=="work"&&File.Exists(Path.Combine(folder,"quality-motion.json")))workMotion=new BakedWorkMotion(folder);
   if(kind=="tea")teaCups=TeaCupLandmarks.For(outfit);
  }
  internal SparseSceneBlend ExitMap(string loop,int pose){return Map(Path.Combine("exit",loop,pose.ToString("00")+".map.gz"));}
  private SparseSceneBlend Map(string name){SparseSceneBlend map;if(!maps.TryGetValue(name,out map)){map=new SparseSceneBlend(Path.Combine(folder,name));maps.Add(name,map);}return map;}
  private static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
  internal Bitmap Frame(ScenePose pose,long elapsed){
   if(workMotion!=null)workMotion.Render(pose,Smooth(pose.Progress),poses,pixels);
   else if(pose.From==pose.To)Array.Copy(poses[pose.From],pixels,pixels.Length);
   else if(Kind=="tea"&&Math.Abs(pose.From-pose.To)==1&&Math.Min(pose.From,pose.To)>=4){
    int first=Math.Min(pose.From,pose.To),last=first+1;
    double progress=Smooth(pose.From==first?pose.Progress:1-pose.Progress);
    SparseSceneBlend map=Map(first.ToString("00")+"-"+last.ToString("00")+".map.gz");
    if(teaCups==null)map.Blend(poses[first],poses[last],progress,pixels);
    else map.BlendTeaCup(poses[first],poses[last],progress,teaCups[first-4],teaCups[last-4],pixels);
   }
   else Map(pose.From.ToString("00")+"-"+pose.To.ToString("00")+".map.gz").Blend(poses[pose.From],poses[pose.To],Smooth(pose.Progress),pixels);
   ScenePixels.Write(output,pixels);
   if(Kind=="bubbles")DrawBubbles(elapsed);
   if(Kind=="fishing")return DrawFishing(pose,elapsed);
   return output;
  }
  internal Bitmap Entry(Image idle,string loop,double progress){int key=Kind=="tea"?TeaCycleTiming.RestPose:0;ExitMap(loop,key).Blend(poses[key],ScenePixels.Read(idle),1-Smooth(progress),pixels);ScenePixels.Write(output,pixels);return output;}
  internal Bitmap ToIdle(Image current,Image idle,string loop,int pose,double progress){ExitMap(loop,pose).Blend(ScenePixels.Read(current),ScenePixels.Read(idle),Smooth(progress),pixels);ScenePixels.Write(output,pixels);return output;}
  internal Bitmap ToPose(Image current,int from,int to,double progress){if(from==to)Array.Copy(poses[to],pixels,pixels.Length);else Map(from.ToString("00")+"-"+to.ToString("00")+".map.gz").Blend(ScenePixels.Read(current),poses[to],Smooth(progress),pixels);ScenePixels.Write(output,pixels);return output;}
  private PointF RingAt(long time){
   ScenePose p=BubbleCycleTiming.At(time);double t=Smooth(p.Progress);
   if(Profile.ring==null||Profile.ring.Length!=12)return new PointF(118,86);
   return ToolAt(p,Profile.ring);
  }
  private PointF ToolAt(ScenePose pose,double[][] positions){
   var from=new PointF((float)positions[pose.From][0],(float)positions[pose.From][1]);
   var to=new PointF((float)positions[pose.To][0],(float)positions[pose.To][1]);
   return pose.From==pose.To?from:Map(pose.From.ToString("00")+"-"+pose.To.ToString("00")+".map.gz").FollowPoint(from,to,Smooth(pose.Progress));
  }
  private Bitmap DrawFishing(ScenePose pose,long elapsed){
   if(fishingSurface==null)fishingSurface=new Bitmap(272,208,PixelFormat.Format32bppPArgb);
   using(var g=Graphics.FromImage(fishingSurface)){
    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
    // Short irregular moving ripples sit below the boat; there is no opaque
    // lake panel or oval backing around the character.
    for(int band=0;band<5;band++){
     var water=new PointF[15];float y=183+band*4.5F;
     for(int i=0;i<water.Length;i++)water[i]=new PointF(13+i*17.4F,y+(float)Math.Sin(i*.85+elapsed/710.0+band)*1.5F);
     using(var line=new Pen(Color.FromArgb(105-band*10,117,190,215),band%2==0?1.8F:1F))g.DrawCurve(line,water,.35F);
    }
    g.DrawImageUnscaled(output,0,0);
    bool casting=pose.Stage=="cast",splash=pose.Stage=="splash",waiting=pose.Stage=="wait"||pose.Stage=="bite",lifting=pose.Stage=="lift";
    if(casting||splash||waiting||lifting){
     double p=Smooth(pose.Progress);
     PointF tip=Profile.rodTip!=null&&Profile.rodTip.Length==12
      ?ToolAt(pose,Profile.rodTip)
      :new PointF(221,61);
     float fx=Profile.floatPoint!=null?(float)Profile.floatPoint[0]:249,fy=Profile.floatPoint!=null?(float)Profile.floatPoint[1]:193;
     bool bite=pose.Stage=="bite";
     fx+=(float)Math.Sin(elapsed/680.0)*.7F;fy+=(float)Math.Sin(elapsed/(bite?55.0:430.0))*(bite?4F:1.1F);
     if(casting){fx=tip.X+(fx-tip.X)*(float)p;fy=tip.Y+(fy-tip.Y)*(float)p-(float)Math.Sin(p*Math.PI)*37;}
     if(lifting){fx+=(tip.X-fx)*(float)p*.25F;fy-=(float)p*60;}
     using(var line=new Pen(Color.FromArgb(190,94,87,109),.8F))g.DrawBezier(line,tip,new PointF(tip.X+6,tip.Y+14),new PointF(fx-8,(tip.Y+fy)*.5F+9),new PointF(fx,fy));
     using(var stem=new Pen(Color.FromArgb(235,251,245,238),1F))g.DrawLine(stem,fx,fy-6,fx,fy+5);
     using(var bobber=new SolidBrush(Color.FromArgb(245,209,81,114)))g.FillEllipse(bobber,fx-2.3F,fy-3,4.6F,7);
     if(waiting||splash){
      float radius=splash?3+(float)p*13:7+(float)Math.Sin(elapsed/400.0)*1.4F;
      using(var ripple=new Pen(Color.FromArgb(splash?(int)(160*(1-p)):135,116,197,220),1F))g.DrawEllipse(ripple,fx-radius,fy+3-radius*.17F,radius*2,radius*.34F);
     }
    }
   }
   return fishingSurface;
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
  public void Dispose(){output.Dispose();if(fishingSurface!=null)fishingSurface.Dispose();if(workMotion!=null)workMotion.Dispose();maps.Clear();}
 }
}

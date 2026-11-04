using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
namespace XiaoJingDesktopPet {
 public sealed partial class PetForm {
  private const int SceneEntryDuration=700;
  private SceneActionPlayer scenePlayer;
  private FishingCycleState fishingCycle;
  private SparseSceneBlend sceneAttentionMap;
  private readonly int[] sceneAttentionPixels=new int[272*208];
  private int[] sceneAttentionSourcePixels,sceneAttentionTargetPixels;
  private Bitmap sceneAttentionOutput;
  private bool sceneAttention;
  private long scenePausedAt=-1;
  private bool scenePausedDuringWorkTea;
  private bool SceneAction {get{return leisure=="bubbles"||leisure=="fishing"||leisure=="tea";}}
  private bool EnsureScenePlayer(string action){
   string outfit=ActionOutfitKey(),identity=outfit+"|"+action;
   if(scenePlayer!=null&&scenePlayer.Identity==identity)return true;
   if(scenePlayer!=null){scenePlayer.Dispose();scenePlayer=null;}
   try{scenePlayer=new SceneActionPlayer(Path.Combine(actionAssetsRoot,"..","scene-actions"),action,outfit);return true;}
   catch(Exception e){assetWarning="新动作素材未能载入："+e.Message;return false;}
  }
  private Bitmap SceneIdleTarget(){
   Image idle;if(!images.TryGetValue("loops/"+CurrentLoopKey()+"/frame-00",out idle))return null;
   var target=new Bitmap(272,208,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(target)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(idle,activeHoliday==null&&CurrentCondition()=="wind"?new Rectangle(8,0,256,208):new Rectangle(40,0,192,208));}return target;
  }
  private ScenePose CurrentScenePose(long now){return leisure=="fishing"&&fishingCycle!=null?fishingCycle.At(now):leisure=="tea"?TeaCycleTiming.At(Math.Max(0,now-leisureStarted-SceneEntryDuration)):BubbleCycleTiming.At(Math.Max(0,now-leisureStarted-SceneEntryDuration));}
  private Bitmap CurrentSceneFrame(long now){
   long elapsed=Math.Max(0,now-leisureStarted-SceneEntryDuration);
   if(now-leisureStarted<SceneEntryDuration)using(var idle=SceneIdleTarget()){if(idle!=null)return scenePlayer.Entry(idle,CurrentLoopKey(),(now-leisureStarted)/(double)SceneEntryDuration);}
   return scenePlayer.Frame(CurrentScenePose(now),elapsed);
  }
  private bool DrawSceneAction(Graphics g,long now){
   if(!SceneAction||!EnsureScenePlayer(leisure))return false;
   float bob=leisure=="fishing"?(float)Math.Sin((now-leisureStarted)/650.0)*.65F:0;
   g.DrawImage(scenePlayer==null?null:CurrentSceneFrame(now),0,122+bob,272,208);return true;
  }
  private bool DrawWorkingTea(Graphics g,long now){
   if(!EnsureScenePlayer("tea"))return false;
   long elapsed=TeaCycleTiming.WorkDuration-(teaUntil-now);
   if(elapsed<SceneEntryDuration)using(var idle=SceneIdleTarget()){if(idle!=null){g.DrawImageUnscaled(scenePlayer.Entry(idle,CurrentLoopKey(),elapsed/(double)SceneEntryDuration),0,122);return true;}}
   long time=Math.Max(0,elapsed-SceneEntryDuration);
   if(time>=TeaCycleTiming.ActionDuration)using(var idle=SceneIdleTarget()){if(idle!=null){g.DrawImageUnscaled(scenePlayer.Entry(idle,CurrentLoopKey(),1-(time-TeaCycleTiming.ActionDuration)/700.0),0,122);return true;}}
   g.DrawImageUnscaled(scenePlayer.Frame(TeaCycleTiming.At(time),time),0,122);return true;
  }
  private float TeaLaptopOpacity(long now){
   if(!SippingAt(now))return 1;
   long elapsed=TeaCycleTiming.WorkDuration-(teaUntil-now);
   if(elapsed<SceneEntryDuration)return (float)(1-WalkSmooth(elapsed/(double)SceneEntryDuration));
   long ending=elapsed-SceneEntryDuration-TeaCycleTiming.ActionDuration;
   return ending<0?0:(float)WalkSmooth(ending/700.0);
  }
  private void StartSceneAction(long now){
   if(!SceneAction)return;
   EnsureScenePlayer(leisure);
   if(leisure=="fishing"){fishingCycle=new FishingCycleState();fishingCycle.Start(now+SceneEntryDuration,5000+random.Next(4500));biteAt=fishingCycle.BiteAt;}
  }
  private void CommitSceneFish(long now,bool interrupt){
   if(fishingCycle==null)return;int? fish=fishingCycle.TakeDeposit(now,interrupt);if(!fish.HasValue)return;
   caughtType=fish.Value;if(fishCollection[caughtType]<9999)fishCollection[caughtType]++;
   caughtAt=now;mood=Clamp(mood+6,0,100);SaveSettings();
  }
  private void StopSceneAction(long now){CommitSceneFish(now,true);fishingCycle=null;}
  private void TickSceneAction(long now){
   if(leisure!="fishing"||fishingCycle==null)return;
   CommitSceneFish(now,false);fishingCycle.Tick(now,5000+random.Next(4500));biteAt=fishingCycle.BiteAt;
  }
  private bool HookSceneFish(long now){
   if(fishingCycle==null)return false;
   int roll=random.Next(100),fish=roll<40?0:roll<65?1:roll<82?2:roll<92?3:roll<98?4:5;
   return fishingCycle.TryHook(now,fish);
  }
  private PointF SceneFloatPoint(){
   if(EnsureScenePlayer("fishing")&&scenePlayer.Profile.floatPoint!=null)return new PointF((float)scenePlayer.Profile.floatPoint[0],122+(float)scenePlayer.Profile.floatPoint[1]);
   return new PointF(256,315);
  }
  private bool BeginSceneAttention(long now){
   if(!SceneAction||fishingCycle!=null&&fishingCycle.CatchActive)return false;
   if(!EnsureScenePlayer(leisure))return false;
   ScenePose pose=CurrentScenePose(now);Bitmap snapshot=(Bitmap)CurrentSceneFrame(now).Clone(),target=SceneIdleTarget();
   if(target==null){snapshot.Dispose();return false;}
   SparseSceneBlend map=scenePlayer.ExitMap(CurrentLoopKey(),pose.Progress<.5?pose.From:pose.To);
   ReleaseAttention();attentionStarted=now;attentionOutfit=ActionOutfitKey();attentionLoop=CurrentLoopKey();attentionSource=snapshot;attentionTarget=target;
   sceneAttentionSourcePixels=ScenePixels.Read(snapshot);sceneAttentionTargetPixels=ScenePixels.Read(target);sceneAttentionMap=map;sceneAttention=true;
   sceneAttentionOutput=new Bitmap(272,208,PixelFormat.Format32bppPArgb);wanderRemaining=0;walkUntil=0;lookUntil=now+2520;return true;
  }
  private bool DrawSceneAttention(Graphics g,long now){
   if(!sceneAttention||sceneAttentionOutput==null)return false;
   sceneAttentionMap.Blend(sceneAttentionSourcePixels,sceneAttentionTargetPixels,WalkSmooth((now-attentionStarted)/720.0),sceneAttentionPixels);ScenePixels.Write(sceneAttentionOutput,sceneAttentionPixels);g.DrawImageUnscaled(sceneAttentionOutput,0,122);return true;
  }
  private void ReleaseSceneAttention(){sceneAttention=false;sceneAttentionMap=null;sceneAttentionSourcePixels=sceneAttentionTargetPixels=null;if(sceneAttentionOutput!=null){sceneAttentionOutput.Dispose();sceneAttentionOutput=null;}}
  private void ReleaseScenePlayer(){if(scenePlayer!=null){scenePlayer.Dispose();scenePlayer=null;}ReleaseSceneAttention();}
  private void PauseSceneActions(long now){scenePausedAt=now;scenePausedDuringWorkTea=SippingAt(now);}
  private void ResumeSceneActions(long now){if(scenePausedAt<0)return;long delta=Math.Max(0,now-scenePausedAt);scenePausedAt=-1;if(SceneAction)leisureStarted+=delta;if(fishingCycle!=null){fishingCycle.ShiftForPause(delta);biteAt=fishingCycle.BiteAt;}if(scenePausedDuringWorkTea)teaUntil+=delta;scenePausedDuringWorkTea=false;if(AttentionActive)attentionStarted+=delta;}
  private string SceneActionHint(long now){
   if(!SceneAction)return null;ScenePose pose=CurrentScenePose(now);
   if(leisure=="bubbles")return now-leisureStarted<SceneEntryDuration?"拿好泡泡棒，准备啦～":pose.Stage=="dip"?"把泡泡棒伸进瓶子，蘸一点泡泡水～":pose.Stage=="raise"?"举到嘴边，准备吹泡泡～":pose.Stage=="inhale"?"轻轻换口气～":pose.Stage=="lower"?"再蘸一点泡泡水～":"呼～吹一串小泡泡";
   if(leisure=="tea")return pose.Stage=="rest"?"托着茶碟歇一会儿～":pose.Stage=="pick"?"托好茶碟，握住杯把～":pose.Stage=="raise"?"双手捧好，送到嘴边～":pose.Stage=="sip"?"小口喝茶，暖暖的～":"把杯子放回手中的茶碟～";
   switch(pose.Stage){case "backswing":return "收好鱼线，向后扬竿～";case "cast":case "splash":return "甩出鱼钩，轻轻落水～";case "bite":return "浮漂动啦！点击浮漂收竿～";case "lift":case "reel":return "拉住鱼线，把鱼儿提上来～";case "unhook":return "小心取下鱼钩～";case "support":case "deposit":return "捧好鱼儿，放进鱼篓～";case "release":case "recast":return "收好啦，再钓一条～";default:return "坐在小船上，等鱼儿咬钩～";}
  }
 }
}

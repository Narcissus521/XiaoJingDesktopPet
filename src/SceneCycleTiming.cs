using System;
namespace XiaoJingDesktopPet {
 internal struct ScenePose {
  internal int From,To; internal double Progress; internal string Stage;
  internal ScenePose(int a,int b,double t,string stage){From=a;To=b;Progress=Math.Max(0,Math.Min(1,t));Stage=stage;}
 }
 internal static class BubbleCycleTiming {
  internal const int Period=8250,EmissionInterval=280,ParticleLife=2100;
  internal static readonly int[] Keys={0,1,2,3,4,5,6,7,7,6,7,11,0};
  internal static readonly int[] Durations={500,650,550,600,600,650,1150,750,650,1000,650,500};
  internal static ScenePose At(long elapsed){long t=Math.Max(0,elapsed)%Period;for(int i=0;i<Durations.Length;i++){if(t<Durations[i])return new ScenePose(Keys[i],Keys[i+1],t/(double)Durations[i],i<3?"dip":i<5?"raise":i==8?"inhale":i<10?"blow":"lower");t-=Durations[i];}return new ScenePose(0,1,0,"dip");}
  internal static bool EmitsAt(long elapsed){long t=Math.Max(0,elapsed)%Period;return t>=3550&&t<5450||t>=6100&&t<7100;}
 }
 internal static class TeaCycleTiming {
  internal const int ActionDuration=8000,RestDuration=12000,Period=ActionDuration+RestDuration,WorkDuration=9400;
  private static readonly int[] Durations={500,600,650,700,600,900,900,700,650,650,650,500};
  internal static ScenePose At(long elapsed){long t=Math.Max(0,elapsed)%Period;if(t>=ActionDuration){long rest=t-ActionDuration,phase=rest%4000;double blink=phase<3820?0:Math.Sin(Math.PI*(phase-3820)/180.0);return new ScenePose(0,10,blink,"rest");}for(int i=0;i<12;i++){if(t<Durations[i])return new ScenePose(i,(i+1)%12,t/(double)Durations[i],i<3?"pick":i<5?"raise":i<7?"sip":"lower");t-=Durations[i];}return new ScenePose(0,1,0,"pick");}
 }
 internal sealed class FishingCycleState {
  internal const int CastDuration=2050,BiteWindow=3400,DepositAfter=5050,CatchDuration=6050;
  private static readonly int[] CatchKeys={4,5,6,7,8,9,11,0};
  private static readonly int[] CatchDurations={800,900,950,900,850,1000,650};
  private static readonly string[] CatchStages={"lift","reel","unhook","support","deposit","release","recast"};
  internal long StartedAt,BiteAt,HookedAt=-1;
  internal int PendingFish=-1;
  internal bool DepositCommitted;
  internal bool CatchActive {get{return HookedAt>=0;}}
  internal void Start(long now,int biteDelay){StartedAt=now;BiteAt=now+Math.Max(CastDuration+1200,biteDelay);HookedAt=-1;PendingFish=-1;DepositCommitted=false;}
  internal bool CanHook(long now){return !CatchActive&&now>=BiteAt&&now-BiteAt<BiteWindow;}
  internal bool TryHook(long now,int fish){if(fish<0||fish>5||!CanHook(now))return false;PendingFish=fish;HookedAt=now;DepositCommitted=false;return true;}
  internal int? TakeDeposit(long now,bool interrupt){if(!CatchActive||DepositCommitted||!interrupt&&now-HookedAt<DepositAfter)return null;DepositCommitted=true;return PendingFish;}
  internal void Tick(long now,int nextBiteDelay){if(CatchActive){if(DepositCommitted&&now-HookedAt>=CatchDuration)Start(now,nextBiteDelay);}else if(now>=BiteAt+BiteWindow)BiteAt=now+Math.Max(1200,nextBiteDelay);}
  internal void ShiftForPause(long milliseconds){if(milliseconds<=0)return;StartedAt+=milliseconds;BiteAt+=milliseconds;if(CatchActive)HookedAt+=milliseconds;}
  internal ScenePose At(long now){
   if(CatchActive){long t=Math.Max(0,now-HookedAt);for(int i=0;i<CatchDurations.Length;i++){if(t<CatchDurations[i])return new ScenePose(CatchKeys[i],CatchKeys[i+1],t/(double)CatchDurations[i],CatchStages[i]);t-=CatchDurations[i];}return new ScenePose(0,0,0,"settled");}
   long elapsed=Math.Max(0,now-StartedAt);if(elapsed<650)return new ScenePose(0,1,elapsed/650.0,"backswing");if(elapsed<1350)return new ScenePose(1,2,(elapsed-650)/700.0,"cast");if(elapsed<CastDuration)return new ScenePose(2,3,(elapsed-1350)/700.0,"splash");
   if(CanHook(now))return new ScenePose(3,4,Math.Min(1,(now-BiteAt)/350.0),"bite");return new ScenePose(3,3,0,"wait");
  }
 }
}

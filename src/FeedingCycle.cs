using System;
namespace XiaoJingDesktopPet {
 internal static class FeedingCycle {
  internal const int Duration=5400, CloseStart=800, CloseEnd=1250, CarryStart=1450, CarryEnd=2600, FoodEnd=3300;
  internal static double Smooth(double p){p=Math.Max(0,Math.Min(1,p));return p*p*(3-2*p);}
  internal static double CarryProgress(long elapsed){return Smooth((elapsed-CarryStart)/(double)(CarryEnd-CarryStart));}
  internal static double CloseProgress(long elapsed){return Smooth((elapsed-CloseStart)/(double)(CloseEnd-CloseStart));}
  internal const int FirstBite=2690, SecondBite=3060;
  internal static double GripOpacity(long elapsed){return Smooth((elapsed-635)/420.0);}
  internal static double Chew(long elapsed){
   double phase=(elapsed-FirstBite)/175.0;
   if(phase<0||elapsed>=3500)return 0;
   return Math.Pow(Math.Sin(phase*Math.PI),2)*Smooth(phase*3)*Smooth((3500-elapsed)/170.0);
  }
  internal static ScenePose At(long elapsed){
   if(elapsed<400)return new ScenePose(8,9,Smooth(elapsed/400.0),"receive");
   if(elapsed<CloseStart)return new ScenePose(9,9,0,"receive");
   if(elapsed<CloseEnd)return new ScenePose(9,24,CloseProgress(elapsed),"close");
   if(elapsed<CarryStart)return new ScenePose(24,24,0,"hold");
   if(elapsed<CarryEnd)return new ScenePose(24,10,CarryProgress(elapsed),"carry");
   if(elapsed<3500)return new ScenePose(10,10,0,"eat");
   if(elapsed<3760)return new ScenePose(10,11,Smooth((elapsed-3500)/260.0),"wipe");
   if(elapsed<4100)return new ScenePose(11,11,0,"wipe");
   if(elapsed<4360)return new ScenePose(11,12,Smooth((elapsed-4100)/260.0),"happy");
   if(elapsed<5000)return new ScenePose(12,12,0,"happy");
   return new ScenePose(12,8,Smooth((elapsed-5000)/400.0),"finish");
  }
 }
}

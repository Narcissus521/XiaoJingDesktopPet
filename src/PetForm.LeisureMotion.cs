using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private LeisureCatalog leisureCatalog;
        private LeisureMotion leisureMotion;
        private MappedTransition attentionMorph;
        private string attentionMorphIdentity;
        private string attentionOutfit,attentionLoop;
        private long attentionStarted=-1,attentionPoseTime;
        private Bitmap attentionTarget,attentionSource;
        private bool AttentionActive {get{return attentionStarted>=0;}}
        private void ReleaseAttention()
        {
            attentionStarted=-1;
            if(attentionTarget!=null){attentionTarget.Dispose();attentionTarget=null;}
            if(attentionSource!=null){attentionSource.Dispose();attentionSource=null;}
        }
        private void ReleaseLeisureMotion()
        {
            ReleaseAttention();if(attentionMorph!=null){attentionMorph.Dispose();attentionMorph=null;attentionMorphIdentity=null;}if(leisureMotion!=null){leisureMotion.Dispose();leisureMotion=null;}
        }
        private bool EnsureLeisureMotion(string action)
        {
            string outfit=ActionOutfitKey(),identity=outfit+"|"+action;
            if(leisureMotion!=null&&leisureMotion.Identity==identity)return true;
            if(leisureMotion!=null){leisureMotion.Dispose();leisureMotion=null;}
            try{
                string root=Path.Combine(actionAssetsRoot,"..","action-motion");
                if(leisureCatalog==null)leisureCatalog=new JavaScriptSerializer().Deserialize<LeisureCatalog>(File.ReadAllText(Path.Combine(root,"catalog.json")));
                LeisureProfile profile;if(leisureCatalog==null||!leisureCatalog.outfits.TryGetValue(outfit,out profile)||!EnsureActionImages(outfit))return false;
                Image source;
                if(action=="bubbles")using(var clean=new Bitmap(Path.Combine(root,outfit+"-bubbles.png")))leisureMotion=new LeisureMotion(outfit,action,clean,profile);
                else if(action=="tea")using(var clean=new Bitmap(Path.Combine(root,outfit+"-bubbles.png")))using(var cup=new Bitmap(Path.Combine(root,outfit+"-tea-cup.png")))leisureMotion=new LeisureMotion(outfit,action,clean,profile,cup);
                else if(images.TryGetValue("actions/"+outfit+"/"+ActionFrameName(action=="tea"?18:action=="book"?16:action=="nap"?17:action=="fishing"?14:action=="rain"?19:action=="sun"?12:8),out source))leisureMotion=new LeisureMotion(outfit,action,source,profile);
                return leisureMotion!=null;
            }catch{if(leisureMotion!=null){leisureMotion.Dispose();leisureMotion=null;}return false;}
        }
        private bool DrawLeisureMotion(Graphics g,long now)
        {
            string action=SippingAt(now)?"tea":leisure;
            if(String.IsNullOrEmpty(action)||FeedingAt(now)||PettingAt(now)||StretchingAt(now)||LandingAt(now)||lifted||falling||activityBusy==true&&!SippingAt(now))return false;
            if(!EnsureLeisureMotion(action))return false;
            long elapsed=SippingAt(now)?6000-(teaUntil-now):now-leisureStarted;
            g.DrawImage(leisureMotion.Frame(elapsed,0),new Rectangle(8,122,256,208));return true;
        }
        private bool PrepareAttentionMorph()
        {
            int pose=leisure=="bubbles"||leisure=="tea"?9:leisure=="book"?16:leisure=="fishing"?14:leisure=="nap"?17:leisure=="rain"?19:leisure=="sun"?12:8;
            string identity=CurrentLoopKey()+"|"+pose;
            if(attentionMorph!=null&&attentionMorphIdentity==identity)return true;
            if(attentionMorph!=null){attentionMorph.Dispose();attentionMorph=null;attentionMorphIdentity=null;}
            try{attentionMorph=new MappedTransition(Path.Combine(actionAssetsRoot,"..","action-motion","attention",CurrentLoopKey(),"pose-"+pose.ToString("00")+".map.gz"));attentionMorphIdentity=identity;return true;}catch{return false;}
        }
        private void BeginAttention(long now)
        {
            if(AttentionActive||String.IsNullOrEmpty(leisure)||!EnsureLeisureMotion(leisure)||!PrepareAttentionMorph())return;
            string loop=CurrentLoopKey();Image idle;if(!images.TryGetValue("loops/"+loop+"/frame-00",out idle))return;
            ReleaseAttention();attentionStarted=now;attentionPoseTime=now-leisureStarted;attentionOutfit=ActionOutfitKey();attentionLoop=loop;
            attentionTarget=new Bitmap(272,208,PixelFormat.Format32bppPArgb);attentionSource=new Bitmap(272,208,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(attentionTarget)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(idle,activeHoliday==null&&CurrentCondition()=="wind"?new Rectangle(8,0,256,208):new Rectangle(40,0,192,208));}
            attentionMorph.SetTarget(attentionTarget);wanderRemaining=0;walkUntil=0;lookUntil=now+2520;
        }
        private void UpdateAttention(long now)
        {
            if(!AttentionActive)return;
            if(activityBusy==true||ActionOutfitKey()!=attentionOutfit||CurrentLoopKey()!=attentionLoop){ReleaseAttention();return;}
            if(now-attentionStarted>=720){leisure="";biteAt=-10000;nextLeisureAt=now+18000;nextWanderAt=now+18000;lookUntil=now+1800;ReleaseAttention();}
        }
        private bool DrawAttention(Graphics g,long now)
        {
            if(!AttentionActive||leisureMotion==null||attentionTarget==null)return false;
            double elapsed=Math.Max(0,now-attentionStarted),away=WalkSmooth(elapsed/340),mix=WalkSmooth((elapsed-280)/440);
            using(Graphics paint=Graphics.FromImage(attentionSource)){
                paint.Clear(Color.Transparent);paint.DrawImageUnscaled(leisureMotion.Frame(attentionPoseTime+(long)(elapsed*.2),away),8,0);
            }
            g.DrawImageUnscaled(attentionMorph.Frame(attentionSource,mix),0,122);return true;
        }
    }
}

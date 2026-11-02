using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private const int WalkTurnMilliseconds=420,WalkSettleMilliseconds=180;
        private readonly Bitmap[] sideWalkSheets=new Bitmap[2];
        private WalkingRig sideWalkingRig;
        private string sideWalkIdentity,sideWalkOutfit;
        private bool sideWalkFacingRight;
        private void ReleaseHomeWalkArtwork()
        {
            for(int i=0;i<2;i++)if(sideWalkSheets[i]!=null){sideWalkSheets[i].Dispose();sideWalkSheets[i]=null;}
            if(sideWalkingRig!=null){sideWalkingRig.Dispose();sideWalkingRig=null;}sideWalkIdentity=null;
        }
        private bool EnsureSideWalkArtwork(string outfit,bool right)
        {
            if(String.IsNullOrEmpty(outfit)||outfit.IndexOfAny(new[]{'/','\\','.',':'})>=0)return false;
            string identity=outfit+"|"+right;if(sideWalkIdentity==identity)return true;
            string root=Path.Combine(actionAssetsRoot,"..","side-walk",outfit),direction=right?"right":"left";
            ReleaseHomeWalkArtwork();
            try{
                sideWalkingRig=new WalkingRig(root);
                for(int i=0;i<2;i++)using(var source=new Bitmap(Path.Combine(root,(i==0?"turn-in-":"turn-out-")+direction+".png"))){
                    if(source.Width!=2176||source.Height!=832)throw new InvalidDataException("Invalid walking turn atlas.");
                    sideWalkSheets[i]=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(sideWalkSheets[i])){g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;g.DrawImageUnscaled(source,0,0);}
                }
                sideWalkIdentity=identity;return true;
            }catch{ReleaseHomeWalkArtwork();return false;}
        }
        private bool PrepareSideWalkArtwork(bool right)
        {
            string outfit=ActionOutfitKey();if(!EnsureSideWalkArtwork(outfit,right))return false;
            sideWalkOutfit=outfit;sideWalkFacingRight=right;return true;
        }
        private static double WalkSmooth(double n){n=Math.Max(0,Math.Min(1,n));return n*n*(3-2*n);}
        private double WalkStride {get{return homeWalkTotalDistance/Math.Max(1,homeWalkCycles)/(sizePercent/100.0);}}
        private double WalkCycle {get{return homeWalkTotalDistance<=0?0:Math.Abs(homeWalkPosition-homeWalkStartX)/homeWalkTotalDistance*homeWalkCycles;}}
        private void DrawWalkTurn(Graphics g,int sheet,double progress)
        {
            int frame=Clamp((int)Math.Round(Math.Max(0,Math.Min(1,progress))*24),0,24);
            g.DrawImage(sideWalkSheets[sheet],new Rectangle(0,122,272,208),new Rectangle(frame%8*272,frame/8*208,272,208),GraphicsUnit.Pixel);
        }
        private void DrawHomeWalking(Graphics g,Image fallback,long now)
        {
            if(!EnsureSideWalkArtwork(sideWalkOutfit,sideWalkFacingRight))return;
            double elapsed=now-homeWalkPhaseStarted;
            if(homeWalkPhase==0){
                if(elapsed<WalkTurnMilliseconds)DrawWalkTurn(g,0,elapsed/WalkTurnMilliseconds);
                else sideWalkingRig.Draw(g,0,WalkStride,WalkSmooth((elapsed-WalkTurnMilliseconds)/WalkSettleMilliseconds),sideWalkFacingRight);
            }else if(homeWalkPhase==1)sideWalkingRig.Draw(g,WalkCycle,WalkStride,1,sideWalkFacingRight);
            else if(elapsed<WalkSettleMilliseconds)sideWalkingRig.Draw(g,0,WalkStride,1-WalkSmooth(elapsed/WalkSettleMilliseconds),sideWalkFacingRight);
            else DrawWalkTurn(g,1,(elapsed-WalkSettleMilliseconds)/WalkTurnMilliseconds);
        }
    }
}

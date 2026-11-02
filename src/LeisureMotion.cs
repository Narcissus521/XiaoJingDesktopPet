using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet
{
    internal sealed class LeisureCatalog
    {
        public int revision;public Dictionary<string,LeisureProfile> outfits;public Dictionary<string,int[]> idleFaces;
    }
    internal sealed class LeisureProfile
    {
        public int bubblePose,removedBubbleComponents;public int[] bubbleEmitter,book,cup,faceBubbles,faceBook,faceTea;
        internal int[] Face(string action){return action=="bubbles"?faceBubbles:action=="book"?faceBook:faceTea;}
    }
    internal sealed class LeisureMotion : IDisposable
    {
        internal readonly string Identity,Action;internal readonly LeisureProfile Profile;
        private readonly MotionSurface surface;
        private readonly MotionInfluence prop,leftHand,rightHand,tail;
        private readonly Bitmap output=new Bitmap(256,208,PixelFormat.Format32bppPArgb);
        private readonly Bitmap teaCup;
        internal LeisureMotion(string id,string action,Image source,LeisureProfile profile,Image cupImage=null)
        {
            Identity=id+"|"+action;Action=action;Profile=profile;surface=new MotionSurface(source);
            if(cupImage!=null)teaCup=new Bitmap(cupImage);
            int[] box=action=="book"?profile.book:profile.cup;
            float cx=box[0]+box[2]/2F,cy=box[1]+box[3]/2F;
            prop=new MotionInfluence(256,208,cx,cy,action=="book"?58:40,32,cx,cy+18,118);
            leftHand=new MotionInfluence(256,208,99,139,29,27,103,119,124,68,117);
            rightHand=new MotionInfluence(256,208,151,133,31,29,146,114,124,143,184);
            tail=new MotionInfluence(256,208,197,173,38,27,178,169,149);
        }
        private static double Smooth(double t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
        private static double TeaLift(double t){t=(t%5200+5200)%5200;return t<1200?Smooth(t/1200):t<2600?1:t<4000?1-Smooth((t-2600)/1400):0;}
        internal Bitmap Frame(long elapsed,double puttingAway)
        {
            elapsed=Math.Max(0,elapsed);puttingAway=Math.Max(0,Math.Min(1,puttingAway));
            double seconds=elapsed/1000.0,phase=seconds*2*Math.PI/4.8;
            surface.Begin();surface.Add(tail,(float)(.035*Math.Sin(phase)),0,0);
            if(Action=="tea"){
                float dy=(float)(-13*TeaLift(elapsed)+puttingAway*10);
                surface.Add(leftHand,(float)(-.12*TeaLift(elapsed)),0,dy);surface.Add(rightHand,(float)(.12*TeaLift(elapsed)),0,dy);
            }else if(Action=="book"){
                surface.Add(prop,(float)(.025*Math.Sin(phase)),(float)Math.Sin(phase)*1.4F,(float)(Math.Sin(phase)*1.2+puttingAway*5));
                surface.Add(leftHand,(float)(.05*Math.Sin(phase)),0,(float)(puttingAway*4));
                surface.Add(rightHand,(float)(-.055*Math.Sin(phase)),0,(float)(puttingAway*4));
            }else if(Action=="bubbles"){
                surface.Add(rightHand,(float)(.10*Math.Sin(phase)),(float)(Math.Sin(phase)*1.2),(float)(puttingAway*12));
                surface.Add(leftHand,(float)(-.025*Math.Sin(phase)),0,0);
            }else{
                surface.Add(leftHand,(float)(.06*Math.Sin(phase)),0,(float)Math.Sin(phase));
                surface.Add(rightHand,(float)(-.06*Math.Sin(phase)),0,(float)Math.Sin(phase));
            }
            using(Graphics g=Graphics.FromImage(output)){
                g.CompositingMode=CompositingMode.SourceCopy;g.DrawImageUnscaled(surface.Finish(),0,0);g.CompositingMode=CompositingMode.SourceOver;
                g.SmoothingMode=SmoothingMode.AntiAlias;
                if(Action=="tea"&&teaCup!=null&&puttingAway<1){float lift=(float)(13*TeaLift(elapsed)-puttingAway*10);using(var attr=new ImageAttributes()){var cm=new ColorMatrix();cm.Matrix33=(float)(1-puttingAway);attr.SetColorMatrix(cm);g.DrawImage(teaCup,Rectangle.Round(new RectangleF(110,121-lift,36,27)),0,0,teaCup.Width,teaCup.Height,GraphicsUnit.Pixel,attr);}}
                if(Action=="bubbles"&&puttingAway<.99)DrawBubbles(g,elapsed,1-puttingAway);
                if(Action=="tea"&&puttingAway<.99)DrawSteam(g,elapsed,1-puttingAway);
                if(Action=="book"&&puttingAway<.8)DrawPage(g,elapsed);
            }
            return output;
        }
        private void DrawBubbles(Graphics g,long elapsed,double visibility)
        {
            float ex=Profile.bubbleEmitter[0],ey=Profile.bubbleEmitter[1];double sway=Math.Sin(elapsed/1000.0*2*Math.PI/4.8);
            ex+=(float)sway*1.2F;ey+=(float)sway*.8F;
            if(Profile.bubblePose==9){
                // Bottle and wand are independent props. The underlying
                // empty-hand artwork contains no permanent soap bubbles.
                float handSway=(float)sway*1.2F;
                using(Pen stem=new Pen(Color.FromArgb((int)(245*visibility),211,185,95),2.5F))g.DrawLine(stem,163+handSway,136,ex-2,ey+4);
                using(Brush bottle=new SolidBrush(Color.FromArgb((int)(245*visibility),65,160,219)))using(Pen outline=new Pen(Color.FromArgb((int)(245*visibility),34,87,135),1.5F)){
                    g.FillRectangle(bottle,84,128,17,22);g.DrawRectangle(outline,84,128,17,22);
                    g.FillRectangle(bottle,88,124,9,5);g.DrawRectangle(outline,88,124,9,5);
                }
                using(Pen wave=new Pen(Color.FromArgb((int)(230*visibility),214,246,255),1.5F))g.DrawBezier(wave,87,140,90,135,94,145,98,139);
            }
            using(Pen wand=new Pen(Color.FromArgb((int)(235*visibility),62,142,183),1.6F))g.DrawEllipse(wand,ex-5,ey-4,10,8);
            // One soap film grows at the wand. Older bubbles detach, drift up
            // and sideways, then pop. Nothing is baked into the body bitmap.
            for(int i=0;i<5;i++){
                double age=((elapsed+i*690)%3450+3450)%3450/3450.0;
                float distance=(float)Math.Max(0,(age-.13)/.87),x=ex+distance*58+(float)Math.Sin(age*5+i)*distance*7,y=ey-distance*72;
                float radius=(float)((4.8+i%3*3.0)*Math.Min(1,age/.14));
                double fade=visibility*(age>.89?1-(age-.89)/.11:1);if(radius<1.1||fade<=0)continue;
                int alpha=(int)(245*fade);
                using(Brush fill=new SolidBrush(Color.FromArgb((int)(135*fade),105,214,246)))g.FillEllipse(fill,x-radius,y-radius,radius*2,radius*2);
                using(Pen shadow=new Pen(Color.FromArgb((int)(215*fade),37,117,167),2.1F))g.DrawEllipse(shadow,x-radius,y-radius,radius*2,radius*2);
                using(Pen rim=new Pen(Color.FromArgb(alpha,198,247,255),1.1F))g.DrawArc(rim,x-radius,y-radius,radius*2,radius*2,185,210);
                using(Pen pink=new Pen(Color.FromArgb((int)(205*fade),255,190,225),1.0F))g.DrawArc(pink,x-radius+1,y-radius+1,radius*2-2,radius*2-2,25,85);
                using(Pen shine=new Pen(Color.FromArgb(alpha,255,255,255),1.6F))g.DrawArc(shine,x-radius+radius*.35F,y-radius+radius*.3F,radius,radius,200,85);
                if(age>.93)using(Pen pop=new Pen(Color.FromArgb((int)(210*fade),179,235,255),1))for(int ray=0;ray<5;ray++){double a=ray*Math.PI*2/5;float r=radius+2+(float)(age-.93)*55;g.DrawLine(pop,x+(float)Math.Cos(a)*r,y+(float)Math.Sin(a)*r,x+(float)Math.Cos(a)*(r+3),y+(float)Math.Sin(a)*(r+3));}
            }
        }
        private void DrawSteam(Graphics g,long elapsed,double visibility)
        {
            int[] c=new[]{110,121,36,27};float drop=(float)(-13*TeaLift(elapsed));
            for(int i=0;i<3;i++){
                float age=(float)((elapsed/2600.0+i*.31)%1),x=c[0]+c[2]*(.2F+i*.3F)+(float)Math.Sin(age*6+i)*2,y=c[1]+drop-age*19;
                using(Pen p=new Pen(Color.FromArgb((int)(175*visibility*Math.Sin(age*Math.PI)),220,242,252),1.5F))g.DrawBezier(p,x,y,x-4,y-3,x+4,y-7,x,y-11);
            }
        }
        private void DrawPage(Graphics g,long elapsed)
        {
            double phase=(elapsed%3600)/3600.0;if(phase<.52||phase>.87)return;
            float p=(float)((phase-.52)/.35);int[] b=Profile.book;float x=b[0]+b[2]*.46F,y=b[1]+2;
            GraphicsState state=g.Save();g.SetClip(new RectangleF(b[0]+3,b[1]-1,b[2]-6,8));
            using(Pen page=new Pen(Color.FromArgb((int)(220*Math.Sin(p*Math.PI)),237,246,250),1.2F))g.DrawBezier(page,x-20*(1-p),y+2,x-10,y-7*(float)Math.Sin(p*Math.PI),x+9,y-7*(float)Math.Sin(p*Math.PI),x+22*p,y+2);
            g.Restore(state);
        }
        public void Dispose(){surface.Dispose();output.Dispose();if(teaCup!=null)teaCup.Dispose();}
    }
}

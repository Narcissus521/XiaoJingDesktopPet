using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
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
        private readonly Bitmap bubbleTools=new Bitmap(256,208,PixelFormat.Format32bppPArgb),bubbleHands=new Bitmap(256,208,PixelFormat.Format32bppPArgb);
        private readonly int[] toolPixels=new int[256*208],handPixels=new int[256*208];
        private readonly bool[] skinMask=new bool[256*208];
        private readonly PointF bottleGrip,wandGrip;
        internal LeisureMotion(string id,string action,Image source,LeisureProfile profile,Image cupImage=null)
        {
            Identity=id+"|"+action;Action=action;Profile=profile;surface=new MotionSurface(source);
            if(cupImage!=null)teaCup=new Bitmap(cupImage);
            bottleGrip=FindPalm(source,false);wandGrip=FindPalm(source,true);
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

        private static bool IsSkin(int c)
        {int a=(int)((uint)c>>24),r=(c>>16)&255,g=(c>>8)&255,b=c&255;return a>150&&r>150&&g>85&&b>75&&r-g>12&&r-b>17&&r-g<100;}
        private static PointF FindPalm(Image source,bool right)
        {
            using(var b=new Bitmap(source)){
                double sx=0,sy=0,total=0;int cx=right?160:100,cy=right?133:135;
                for(int y=124;y<150;y++)for(int x=right?148:85;x<(right?177:113);x++){
                    if(!IsSkin(b.GetPixel(x,y).ToArgb()))continue;
                    double w=1/(1+((x-cx)*(x-cx)+(y-cy)*(y-cy))/24.0);sx+=x*w;sy+=y*w;total+=w;
                }
                return total>0?new PointF((float)(sx/total),(float)(sy/total)):new PointF(cx,cy);
            }
        }
        private static float ToolAngle(MotionSurface surface,PointF grip)
        {
            PointF a=surface.FollowSourcePoint(grip),b=surface.FollowSourcePoint(new PointF(grip.X,grip.Y-7));
            return (float)(Math.Atan2(b.Y-a.Y,b.X-a.X)*180/Math.PI+90);
        }
        private PointF DrawBubbleTools(Graphics canvas,long elapsed,double visibility)
        {
            PointF left=surface.FollowSourcePoint(bottleGrip),right=surface.FollowSourcePoint(wandGrip),emitter;
            using(Graphics g=Graphics.FromImage(bubbleTools)){
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                GraphicsState saved=g.Save();g.TranslateTransform(left.X,left.Y);g.RotateTransform(ToolAngle(surface,bottleGrip));
                using(var body=new GraphicsPath()){
                    body.AddBezier(-4,1,-9,2,-10,6,-10,12);body.AddBezier(-10,12,-11,21,-7,24,0,24);
                    body.AddBezier(0,24,8,24,11,21,10,14);body.AddBezier(10,14,10,4,7,2,4,1);body.CloseFigure();
                    g.TranslateTransform(1,1.2F);using(var shadow=new SolidBrush(Color.FromArgb(50,26,37,64)))g.FillPath(shadow,body);g.TranslateTransform(-1,-1.2F);
                    using(var fill=new LinearGradientBrush(new Rectangle(-11,0,23,25),Color.FromArgb(206,245,255),Color.FromArgb(48,113,171),48))g.FillPath(fill,body);
                    GraphicsState clipped=g.Save();g.SetClip(body);
                    using(var water=new GraphicsPath()){
                        water.AddBezier(-12,12,-5,9,4,16,12,12);water.AddLine(12,12,12,27);water.AddLine(12,27,-12,27);water.CloseFigure();
                        using(var fill=new LinearGradientBrush(new Rectangle(-12,10,24,17),Color.FromArgb(155,66,181,219),Color.FromArgb(210,53,101,167),90))g.FillPath(fill,water);
                    }
                    using(var shine=new Pen(Color.FromArgb(205,244,254,255),1.9F){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawBezier(shine,-6,5,-8,9,-7,14,-7,16);
                    using(var reflected=new Pen(Color.FromArgb(110,187,239,255),.8F))g.DrawArc(reflected,-6,14,13,7,12,125);
                    g.Restore(clipped);
                    using(var edge=new Pen(Color.FromArgb(41,44,67),1.45F))g.DrawPath(edge,body);
                }
                using(var neck=new LinearGradientBrush(new Rectangle(-5,-4,10,8),Color.FromArgb(135,210,233),Color.FromArgb(54,104,154),0F))g.FillRectangle(neck,-4,-3,8,6);
                using(var cap=new Pen(Color.FromArgb(42,48,73),4.6F){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(cap,-4,-3,4,-3);
                using(var trim=new Pen(Color.FromArgb(230,196,123),1.2F))g.DrawLine(trim,-4,-4,4,-4);
                using(var label=new SolidBrush(Color.FromArgb(233,236,248,250)))g.FillEllipse(label,-5,8,11,10);
                using(var whale=new SolidBrush(Color.FromArgb(58,86,131))){g.FillEllipse(whale,-3,12,6,3.4F);g.FillPolygon(whale,new[]{new PointF(2,13),new PointF(5,10),new PointF(5,14)});}
                using(var eye=new SolidBrush(Color.FromArgb(235,250,255)))g.FillEllipse(eye,-2,12.8F,.8F,.8F);
                g.Restore(saved);saved=g.Save();g.TranslateTransform(right.X,right.Y);g.RotateTransform(ToolAngle(surface,wandGrip));
                using(var edge=new Pen(Color.FromArgb(41,44,67),3.5F){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(edge,0,5,10,-19);
                using(var stem=new LinearGradientBrush(new Rectangle(0,-20,11,26),Color.FromArgb(255,228,158),Color.FromArgb(181,126,69),0F))
                using(var pen=new Pen(stem,1.9F){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(pen,0,4,10,-19);
                using(var rim=new Pen(Color.FromArgb(40,54,79),2.8F))g.DrawEllipse(rim,5,-28,11,9);
                using(var rim=new Pen(Color.FromArgb(101,189,213),1.55F))g.DrawEllipse(rim,5,-28,11,9);
                using(var film=new LinearGradientBrush(new Rectangle(6,-27,9,7),Color.FromArgb(75,218,249,255),Color.FromArgb(30,144,207,232),65))g.FillEllipse(film,6,-27,9,7);
                using(var highlight=new Pen(Color.FromArgb(233,247,249),.8F))g.DrawArc(highlight,5.5F,-27.5F,10,8,190,135);
                PointF[] points={new PointF(10.5F,-23.5F)};using(Matrix matrix=g.Transform)matrix.TransformPoints(points);emitter=points[0];g.Restore(saved);
            }
            using(var attributes=new ImageAttributes()){
                var color=new ColorMatrix();color.Matrix33=(float)visibility;attributes.SetColorMatrix(color);
                canvas.DrawImage(bubbleTools,new Rectangle(0,0,256,208),0,0,256,208,GraphicsUnit.Pixel,attributes);
            }
            DrawHandsOverTools(canvas);return emitter;
        }
        private void DrawHandsOverTools(Graphics g)
        {
            var area=new Rectangle(0,0,256,208);var data=surface.Output.LockBits(area,ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(data.Scan0,toolPixels,0,toolPixels.Length);}finally{surface.Output.UnlockBits(data);}
            Array.Clear(handPixels,0,handPixels.Length);Array.Clear(skinMask,0,skinMask.Length);
            for(int y=120;y<155;y++)for(int x=77;x<186;x++)if(x<119||x>143)skinMask[y*256+x]=IsSkin(toolPixels[y*256+x]);
            for(int y=120;y<155;y++)for(int x=77;x<186;x++){
                int i=y*256+x;if((x<119||x>143)&&(skinMask[i]||skinMask[i-1]||skinMask[i+1]||skinMask[i-256]||skinMask[i+256]))handPixels[i]=toolPixels[i];
            }
            data=bubbleHands.LockBits(area,ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(handPixels,0,data.Scan0,handPixels.Length);}finally{bubbleHands.UnlockBits(data);}
            g.DrawImageUnscaled(bubbleHands,0,0);
        }

        private void DrawBubbles(Graphics g,long elapsed,double visibility)
        {
            PointF emitter=DrawBubbleTools(g,elapsed,visibility);
            float ex=emitter.X,ey=emitter.Y;
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
        public void Dispose(){surface.Dispose();output.Dispose();bubbleTools.Dispose();bubbleHands.Dispose();if(teaCup!=null)teaCup.Dispose();}
    }
}

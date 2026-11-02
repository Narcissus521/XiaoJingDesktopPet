using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Web.Script.Serialization;
namespace XiaoJingDesktopPet
{
    internal sealed class WalkingRigData
    {
        public string id;public int ground,cut;public double[] armHand,armPivot,armCenter,armRadii;public WalkingFootData[] feet;
    }
    internal sealed class WalkingFootData
    {
        public double[] hip,neutralHip,offset,lengths;public double ankleX,ankleY,baseAnkleY,width,sole;public int[][] profile;
    }
    internal sealed class LegPose
    {
        internal PointF Hip,Knee,Ankle;internal bool Stance;
    }
    internal sealed class WalkingRig : IDisposable
    {
        internal readonly WalkingRigData Data;
        private readonly Bitmap body;
        private readonly Bitmap[] shoes=new Bitmap[2];
        private readonly MotionSurface surface;
        private readonly MotionInfluence arm;
        private readonly Pen[] outlines=new Pen[2];
        private readonly Pen[][] bands=new Pen[2][];
        private readonly Brush[] kneeBrushes=new Brush[2];
        internal WalkingRig(string root)
        {
            try {
            Data=new JavaScriptSerializer().Deserialize<WalkingRigData>(File.ReadAllText(Path.Combine(root,"rig.json")));
            if(Data==null||Data.feet==null||Data.feet.Length!=2)throw new InvalidDataException("Incomplete walking rig.");
            body=new Bitmap(Path.Combine(root,"body.png"));if(body.Width!=272||body.Height!=208)throw new InvalidDataException("Invalid walking body.");
            surface=new MotionSurface(body);arm=new MotionInfluence(272,208,(float)Data.armCenter[0],(float)Data.armCenter[1],(float)Data.armRadii[0],(float)Data.armRadii[1],(float)Data.armPivot[0],(float)Data.armPivot[1],128);
            for(int i=0;i<2;i++){
                var f=Data.feet[i];shoes[i]=new Bitmap(Path.Combine(root,"foot-"+i+".png"));outlines[i]=new Pen(Color.FromArgb(25,29,48),(float)f.width+1.5F){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};bands[i]=new Pen[f.profile.Length];
                double tone=i==0?1:.88;
                for(int j=0;j<f.profile.Length;j++){int[] c=f.profile[j];bands[i][j]=new Pen(Color.FromArgb((int)(c[0]*tone),(int)(c[1]*tone),(int)(c[2]*tone)),(float)(f.width*(1-.18*j/f.profile.Length))){StartCap=LineCap.Flat,EndCap=LineCap.Flat};}
                int[] kc=f.profile[Math.Min(f.profile.Length-1,(int)Math.Round(f.profile.Length*.52))];kneeBrushes[i]=new SolidBrush(Color.FromArgb((int)(kc[0]*tone),(int)(kc[1]*tone),(int)(kc[2]*tone)));
            }
            } catch { Dispose();throw; }
        }
        private static PointF Lerp(PointF a,PointF b,double t){return new PointF((float)(a.X+(b.X-a.X)*t),(float)(a.Y+(b.Y-a.Y)*t));}
        internal LegPose Pose(int index,double phase,double stride,double amount)
        {
            var f=Data.feet[index];double p=(phase+index*.5)%1,step=stride/4,x,lift=0;
            if(p<.5)x=-step+4*step*p;
            else{double s=(p-.5)*2,v=2*step;x=(2*s*s*s-3*s*s+1)*step+(s*s*s-2*s*s+s)*v+(-2*s*s*s+3*s*s)*(-step)+(s*s*s-s*s)*v;lift=6.5*Math.Pow(Math.Sin(Math.PI*s),2);}
            PointF activeHip=new PointF((float)f.hip[0],(float)f.hip[1]),activeAnkle=new PointF((float)(f.hip[0]+x),(float)(f.baseAnkleY-lift));
            double vx=activeAnkle.X-activeHip.X,vy=activeAnkle.Y-activeHip.Y,d=Math.Max(.001,Math.Sqrt(vx*vx+vy*vy)),ux=vx/d,uy=vy/d,l1=f.lengths[0],l2=f.lengths[1],a=(l1*l1-l2*l2+d*d)/(2*d),h=Math.Sqrt(Math.Max(0,l1*l1-a*a));
            PointF activeKnee=new PointF((float)(activeHip.X+ux*a-uy*h),(float)(activeHip.Y+uy*a+ux*h));
            PointF neutralHip=new PointF((float)f.neutralHip[0],(float)f.neutralHip[1]),neutralAnkle=new PointF((float)f.ankleX,(float)f.baseAnkleY),neutralKnee=Lerp(neutralHip,neutralAnkle,.52);
            return new LegPose{Hip=Lerp(neutralHip,activeHip,amount),Knee=Lerp(neutralKnee,activeKnee,amount),Ankle=Lerp(neutralAnkle,activeAnkle,amount),Stance=p<.5};
        }
        internal void Draw(Graphics g,double phase,double stride,double amount,bool right)
        {
            GraphicsState state=g.Save();g.TranslateTransform(0,122);if(right){g.TranslateTransform(272,0);g.ScaleTransform(-1,1);}
            DrawLeg(g,1,Pose(1,phase,stride,amount));DrawLeg(g,0,Pose(0,phase,stride,amount));
            surface.Begin();surface.Add(arm,(float)(.25*Math.Cos(phase*2*Math.PI)*amount),0,0);g.DrawImageUnscaled(surface.Finish(),0,0);g.Restore(state);
        }
        private PointF PointAlong(LegPose p,double t){return t<.52?Lerp(p.Hip,p.Knee,t/.52):Lerp(p.Knee,p.Ankle,(t-.52)/.48);}
        private void DrawLeg(Graphics g,int index,LegPose p)
        {
            var f=Data.feet[index];g.DrawLines(outlines[index],new[]{p.Hip,p.Knee,p.Ankle});int n=bands[index].Length;
            for(int j=0;j<n;j++)g.DrawLine(bands[index][j],PointAlong(p,j/(double)n),PointAlong(p,(j+1)/(double)n));
            float r=(float)f.width*.44F;g.FillEllipse(kneeBrushes[index],p.Knee.X-r,p.Knee.Y-r,r*2,r*2);
            RectangleF dest=new RectangleF((float)(p.Ankle.X-f.offset[0]),(float)(p.Ankle.Y-f.offset[1]),shoes[index].Width,shoes[index].Height);
            if(index==0)g.DrawImage(shoes[index],dest);
            else using(var attributes=new System.Drawing.Imaging.ImageAttributes()){var color=new System.Drawing.Imaging.ColorMatrix();color.Matrix00=color.Matrix11=color.Matrix22=.88F;attributes.SetColorMatrix(color);g.DrawImage(shoes[index],Rectangle.Round(dest),0,0,shoes[index].Width,shoes[index].Height,GraphicsUnit.Pixel,attributes);}
        }
        public void Dispose()
        {
            if(surface!=null)surface.Dispose();if(body!=null)body.Dispose();for(int i=0;i<2;i++){if(shoes[i]!=null)shoes[i].Dispose();if(outlines[i]!=null)outlines[i].Dispose();if(kneeBrushes[i]!=null)kneeBrushes[i].Dispose();if(bands[i]!=null)foreach(Pen p in bands[i])if(p!=null)p.Dispose();}
        }
    }
}

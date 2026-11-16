using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web.Script.Serialization;

namespace XiaoJingDesktopPet
{
    internal sealed class CharacterActionProfile
    {
        public int count { get; set; }
        public int[][] edges { get; set; }
    }

    internal sealed class FeedingCarryProfile {
        public int revision {get;set;} public int count {get;set;}
        public double[][] foodPoints {get;set;}
        public double[][] closePoints {get;set;}
        public bool gloved {get;set;}
    }

    internal sealed class CharacterActionPlayer : IDisposable
    {
        internal readonly string Outfit;
        private readonly string folder;
        private readonly int[][] poses;
        private readonly HashSet<string> edges = new HashSet<string>();
        private readonly Dictionary<string, SparseSceneBlend> maps = new Dictionary<string, SparseSceneBlend>();
        private readonly int[] pixels = new int[256 * 208];
        private readonly Bitmap output = new Bitmap(256, 208, PixelFormat.Format32bppPArgb);
        private int current = 8, target = 8;
        private long started = -1, cachedAt = -1, idleStarted;
        private int duration = 240, cachedDesired = -1, cachedWind = -1;
        private BakedPairClip feedingCarry;
        private BakedPairClip feedingClose;
        private double[][] feedingFoodPoints;
        private double[][] feedingClosePoints;
        private bool feedingGloved;
        private readonly int[] feedingFrontPixels=new int[256*208];
        private readonly bool[] feedingFrontMask=new bool[256*208];
        private readonly Bitmap feedingFront=new Bitmap(256,208,PixelFormat.Format32bppPArgb);
        private readonly int[] feedingCarryPixels = new int[272 * 208];

        internal CharacterActionPlayer(string root, string outfit)
        {
            Outfit = outfit; folder = Path.Combine(root, outfit);
            var profile = new JavaScriptSerializer().Deserialize<CharacterActionProfile>(File.ReadAllText(Path.Combine(folder, "profile.json")));
            if (profile == null || profile.count != 24 || profile.edges == null)
                throw new InvalidDataException("Incomplete dragon action profile.");
            poses = new int[profile.count][];
            for (int index = 0; index < poses.Length; index++)
                using (var image = new Bitmap(Path.Combine(folder, "pose-" + index.ToString("00") + ".png"))) {
                    if (image.Width != 256 || image.Height != 208) throw new InvalidDataException("Invalid dragon pose dimensions.");
                    poses[index] = ScenePixels.Read(image);
                }
            foreach (int[] edge in profile.edges) edges.Add(Name(edge[0], edge[1]));
            string carryPath=Path.Combine(folder,"feeding-carry.vfi");
            if(File.Exists(carryPath)){
                var carryProfile=new JavaScriptSerializer().Deserialize<FeedingCarryProfile>(File.ReadAllText(Path.Combine(folder,"feeding-carry.json")));
                if(carryProfile==null||(carryProfile.revision!=402&&carryProfile.revision!=403)||carryProfile.count!=65||carryProfile.foodPoints==null||carryProfile.foodPoints.Length!=65)throw new InvalidDataException("Invalid feeding palm motion.");
                foreach(double[] p in carryProfile.foodPoints)if(p==null||p.Length!=2||Double.IsNaN(p[0])||Double.IsNaN(p[1])||p[0]<0||p[0]>256||p[1]<0||p[1]>208)throw new InvalidDataException("Invalid feeding palm position.");
                feedingFoodPoints=carryProfile.foodPoints;feedingCarry=new BakedPairClip(carryPath,65);
                if(carryProfile.revision==403){
                    if(carryProfile.closePoints==null||carryProfile.closePoints.Length!=33)throw new InvalidDataException("Missing grasp transition.");
                    foreach(double[] p in carryProfile.closePoints)if(p==null||p.Length!=2||Double.IsNaN(p[0])||Double.IsNaN(p[1])||p[0]<0||p[0]>256||p[1]<0||p[1]>208)throw new InvalidDataException("Invalid grasp palm position.");
                    feedingClosePoints=carryProfile.closePoints;feedingGloved=carryProfile.gloved;
                    feedingClose=new BakedPairClip(Path.Combine(folder,"feeding-close.vfi"),33);
                }else foreach(double[] p in feedingFoodPoints)p[1]+=10;
            }
        }

        private static string Name(int a, int b) { return a.ToString("00") + "-" + b.ToString("00"); }
        private static double Smooth(double t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        private void Blend(int a, int b, double progress)
        {
            if (a == b) { Array.Copy(poses[a], pixels, pixels.Length); return; }
            string name = Name(a, b); SparseSceneBlend map;
            if (!maps.TryGetValue(name, out map)) { map = new SparseSceneBlend(Path.Combine(folder, name + ".map.gz")); maps.Add(name, map); }
            map.Blend(poses[a], poses[b], progress, pixels);
        }

        internal Bitmap Frame(int desired, long now, bool held, int windSeverity = 0)
        {
            desired = desired < 0 ? 8 : desired;
            if (desired >= poses.Length) desired = 8;
            if (cachedAt == now && cachedDesired == desired && cachedWind == windSeverity) return output;
            cachedAt = now; cachedDesired = desired; cachedWind = windSeverity;
            if (started >= 0 && now - started >= duration) { current = target; started = -1; if(current==8)idleStarted=now; }
            if (started < 0 && current != desired) {
                target = edges.Contains(Name(current, desired)) ? desired : 8;
                if (target == current) target = desired;
                duration = desired == 20 || desired == 21 ? 115 : held ? 200 : 260;
                started = now;
            }
            if (started >= 0) Blend(current, target, Smooth((now - started) / (double)duration));
            else if (current == 8) {
                // Two independently drawn neutral poses keep the hair, wings,
                // sleeves and tail alive without deforming isolated old limbs.
                int period = windSeverity == 1 ? 3200 : windSeverity == 2 ? 2200 : windSeverity >= 3 ? 1600 : 4800;
                double amount = windSeverity == 1 ? .45 : windSeverity == 2 ? .7 : 1;
                if(idleStarted<0)idleStarted=now;
                double phase = (Math.Max(0, now-idleStarted) % period) / (double)period;
                Blend(8, 5, amount * (.5 - .5 * Math.Cos(phase * Math.PI * 2)));
            } else if (current == 0 && held) {
                double phase = (Math.Max(0, now) % 1300) / 1300.0;
                Blend(0, 1, .5 - .5 * Math.Cos(phase * Math.PI * 2));
            } else Array.Copy(poses[current], pixels, pixels.Length);
            ScenePixels.Write(output, pixels); return output;
        }

        internal Bitmap FeedingFrame(long elapsed)
        {
            ScenePose pose=FeedingCycle.At(elapsed);
            if(pose.Stage=="close"&&feedingClose!=null){
                feedingClose.Sample(pose.Progress,feedingCarryPixels);
                for(int row=0;row<208;row++)Array.Copy(feedingCarryPixels,row*272+8,pixels,row*256,256);
            }else if(pose.Stage=="hold"&&feedingCarry!=null){
                feedingCarry.Sample(0,feedingCarryPixels);
                for(int row=0;row<208;row++)Array.Copy(feedingCarryPixels,row*272+8,pixels,row*256,256);
            }else if(pose.Stage=="carry"&&feedingCarry!=null){
                feedingCarry.Sample(pose.Progress,feedingCarryPixels);
                for(int row=0;row<208;row++)Array.Copy(feedingCarryPixels,row*272+8,pixels,row*256,256);
            }else Blend(pose.From==24?9:pose.From,pose.To==24?9:pose.To,pose.Progress);
            // The feeding sequence owns a single clock, independent of the
            // previous idle/work pose and of how often the window was drawn.
            current=target=pose.To==24?9:pose.To;started=-1;cachedAt=-1;idleStarted=-1;
            ScenePixels.Write(output,pixels);return output;
        }

        internal PointF FeedingPoint(PointF hand, PointF mouth, long elapsed)
        {
            if(elapsed<FeedingCycle.CloseStart)return hand;
            if(feedingClosePoints!=null&&elapsed<FeedingCycle.CarryStart)return PointAt(feedingClosePoints,FeedingCycle.CloseProgress(elapsed));
            double progress=FeedingCycle.CarryProgress(elapsed);
            if(feedingFoodPoints!=null){
                return PointAt(feedingFoodPoints,progress);
            }
            return new PointF(hand.X+(mouth.X-hand.X)*(float)progress,
                              hand.Y+(mouth.Y-hand.Y)*(float)progress);
        }

        private static PointF PointAt(double[][] points,double progress){
            double at=Math.Max(0,Math.Min(1,progress))*(points.Length-1);int first=Math.Min(points.Length-1,(int)at),next=Math.Min(points.Length-1,first+1);double p=at-first;
            return new PointF((float)(points[first][0]+(points[next][0]-points[first][0])*p),(float)(points[first][1]+(points[next][1]-points[first][1])*p));
        }

        internal void DrawFeedingFingers(Graphics g,PointF support,float width,float height,long elapsed){
            double opacity=FeedingCycle.GripOpacity(elapsed);if(opacity<=0)return;
            Array.Clear(feedingFrontPixels,0,feedingFrontPixels.Length);
            int left=Math.Max(0,(int)(support.X-width*.55F-2)),right=Math.Min(255,(int)(support.X+width*.55F+2));
            int top=Math.Max(0,(int)(support.Y-Math.Min(10,height*.6F))),bottom=Math.Min(207,(int)(support.Y+2));
            var selected=feedingFrontMask;Array.Clear(selected,0,selected.Length);
            for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++){
                int v=pixels[y*256+x],a=(v>>24)&255,r=(v>>16)&255,b=v&255,green=(v>>8)&255;
                bool skin=a>160&&r>140&&green>100&&r-green>6&&r-b>8;
                double gloveX=Math.Abs(x-support.X)-7,gloveY=y-support.Y-2;
                bool mitten=feedingGloved&&gloveX*gloveX/49+gloveY*gloveY/49<=1&&a>200&&r>195&&green>195&&b>195&&Math.Abs(r-green)<35;
                if((skin||mitten)&&BelowFeedingJaw(x,y))selected[y*256+x]=true;
            }
            for(int y=top;y<=bottom;y++)for(int x=left;x<=right;x++){
                int i=y*256+x;bool front=selected[i]||(x>0&&selected[i-1])||(x<255&&selected[i+1])||(y>0&&selected[i-256])||(y<207&&selected[i+256]);
                if(front&&BelowFeedingJaw(x,y))feedingFrontPixels[i]=pixels[i];
            }
            ScenePixels.Write(feedingFront,feedingFrontPixels);
            if(opacity>=.999)g.DrawImageUnscaled(feedingFront,8,122);
            else using(var attributes=new ImageAttributes()){
                var matrix=new System.Drawing.Imaging.ColorMatrix();matrix.Matrix33=(float)opacity;attributes.SetColorMatrix(matrix);
                g.DrawImage(feedingFront,new Rectangle(8,122,256,208),0,0,256,208,GraphicsUnit.Pixel,attributes);
            }
        }

        private bool BelowFeedingJaw(int x,int y){
            if(feedingFoodPoints==null)return true;
            double[] mouth=feedingFoodPoints[feedingFoodPoints.Length-1];
            // Follow the V-shaped lower jaw, not a horizontal crop across both
            // hands. The authored endpoint is ten pixels below the lips.
            return y>=mouth[1]-1-Math.Abs(x-(mouth[0]+3))*.65;
        }

        public void Dispose() {
            if(feedingCarry!=null)feedingCarry.Dispose();
            if(feedingClose!=null)feedingClose.Dispose();feedingFront.Dispose();
            output.Dispose(); maps.Clear();
            }
    }
}


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
        private readonly FeedingDepth feedingDepth;
        private readonly int[] feedingJawPixels = new int[256 * 208];
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
            string depthPath=Path.Combine(folder,"feeding-depth.bin");
            if(File.Exists(depthPath)) feedingDepth=new FeedingDepth(depthPath);
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
            if(pose.Stage=="eat")AnimateFeedingJaw(FeedingCycle.Chew(elapsed));
            // The feeding sequence owns a single clock, independent of the
            // previous idle/work pose and of how often the window was drawn.
            current=target=pose.To==24?9:pose.To;started=-1;cachedAt=-1;idleStarted=-1;
            ScenePixels.Write(output,pixels);return output;
        }

        internal PointF FeedingPoint(PointF hand, PointF mouth, long elapsed)
        {
            if(feedingDepth!=null){feedingDepth.Sample(elapsed);return feedingDepth.Support;}
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

        private void AnimateFeedingJaw(double amount)
        {
            if(amount<=0||feedingFoodPoints==null)return;
            double[] lip=feedingFoodPoints[feedingFoodPoints.Length-1];
            int cx=(int)Math.Round(lip[0]), cy=(int)Math.Round(lip[1]-10);
            Array.Copy(pixels,feedingJawPixels,pixels.Length);
            for(int y=Math.Max(1,cy-3);y<Math.Min(208,cy+9);y++)
                for(int x=Math.Max(0,cx-10);x<Math.Min(256,cx+11);x++){
                    double horizontal=1-Math.Abs(x-cx)/11.0;
                    double vertical=1-Math.Abs(y-cy-2)/7.0;
                    int weight=(int)(190*amount*horizontal*Math.Max(0,vertical));
                    int index=y*256+x;
                    pixels[index]=MixPixels(feedingJawPixels[index],feedingJawPixels[index-256],weight);
                }
        }

        private static int MixPixels(int a,int b,int weight)
        {
            int result=0;
            for(int shift=0;shift<=24;shift+=8)
                result|=((((a>>shift)&255)*(256-weight)+((b>>shift)&255)*weight+128)>>8)<<shift;
            return result;
        }

        internal double FeedingHandCover(int x,int y,PointF hand,PointF lips,long elapsed)
        {
            if(feedingDepth!=null)return feedingDepth.Coverage(x,y);
            if(x<1||x>=255||y<1||y>=207)return 0;
            double dx=x-hand.X,dy=y-hand.Y;
            double jaw=lips.Y+8-Math.Abs(x-(lips.X+3))*.55;
            double faceGuard=FeedingCycle.Smooth((y-jaw+1)/3);
            // Confine the matte to the grip; coat trim and face never become
            // cutouts painted over the food. Sample adjacent outline pixels.
            double zoneX=FeedingCycle.Smooth((18-Math.Abs(dx))/4);
            double zoneY=FeedingCycle.Smooth((dy+10)/3)*FeedingCycle.Smooth((6-dy)/3);
            if(faceGuard*zoneX*zoneY==0)return 0;
            int index=y*256+x;
            double skin=FeedingHandColor(pixels[index]);
            if(skin<.97){
                double edge=Math.Max(Math.Max(FeedingHandColor(pixels[index-1]),FeedingHandColor(pixels[index+1])),
                                     Math.Max(FeedingHandColor(pixels[index-256]),FeedingHandColor(pixels[index+256])));
                skin=Math.Max(skin,edge*.80);
            }
            if(feedingGloved){
                double fingerX=Math.Abs(dx)-7;
                double glove=FeedingCycle.Smooth(1.2-(fingerX*fingerX+(dy-1)*(dy-1))/49);
                int value=pixels[index];
                if(((value>>24)&255)>210&&(value&255)>165&&((value>>16)&255)>165)
                    skin=Math.Max(skin,glove);
            }
            return skin*zoneX*zoneY*faceGuard*FeedingCycle.GripOpacity(elapsed);
        }

        private static double FeedingHandColor(int value)
        {
            int alpha=(value>>24)&255,red=(value>>16)&255,green=(value>>8)&255,blue=value&255;
            if(alpha<160||red<125||green<94)return 0;
            return FeedingCycle.Smooth((Math.Min(red-green,red-blue)-3)/8.0);
        }

        public void Dispose() {
            if(feedingCarry!=null)feedingCarry.Dispose();
            if(feedingClose!=null)feedingClose.Dispose();
            output.Dispose(); maps.Clear();
            }
    }
}


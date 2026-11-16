using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private static readonly string[] FlightPhases = { "launch", "cruise", "approach", "land" };
        private static readonly int[] FlightCounts = { 49, 48, 49, 49 };
        private readonly Bitmap[] flightSheets = new Bitmap[4];
        private Rectangle[][] flightOpaqueBounds;
        private Rectangle flightMotionBounds;
        private sealed class FlightBoundsProfile {
            public int revision {get;set;}
            public Dictionary<string,int[][]> left {get;set;}
            public Dictionary<string,int[][]> right {get;set;}
        }
        private string flightSheetKey, flightOutfit;
        private long flightLandingStarted = -10000;
        private bool FlightFacingRight { get { return flightTo.X > flightFrom.X; } }
        private string FlightOutfitKey() { return ActionOutfitKey(); }
        private bool FlightLandingAt(long now)
        { return !returnFlying && !lifted && !falling && LandingAt(now) && now >= flightLandingStarted && now - flightLandingStarted < 650; }
        private void ReleaseFlightArtwork()
        {
            for (int i = 0; i < flightSheets.Length; i++) { if (flightSheets[i] != null) { flightSheets[i].Dispose(); flightSheets[i] = null; } }
            flightSheetKey = null;
            flightOpaqueBounds = null;
            flightMotionBounds = Rectangle.Empty;
        }
        private bool EnsureFlightArtwork(string outfit, bool right)
        {
            if (String.IsNullOrEmpty(outfit) || outfit.IndexOfAny(new char[] {'/', '\\', '.', ':'}) >= 0) return false;
            string direction = right ? "right" : "left", identity = outfit + "|" + direction;
            if (flightSheetKey == identity) return true;
            string root = Path.Combine(actionAssetsRoot, "..", "flight", outfit);
            for (int i = 0; i < 4; i++) if (!File.Exists(Path.Combine(root, FlightPhases[i] + "-" + direction + ".png"))) return false;
            ReleaseFlightArtwork();
            try {
                for (int i = 0; i < 4; i++) {
                    using (var source = new Bitmap(Path.Combine(root, FlightPhases[i] + "-" + direction + ".png"))) {
                        if (source.Width != 2176 || source.Height != 208 * ((FlightCounts[i] + 7) / 8)) throw new InvalidDataException("Invalid flight sprite dimensions.");
                        flightSheets[i] = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(flightSheets[i])) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImageUnscaled(source, 0, 0); }
                    }
                }
                string boundsFile=Path.Combine(root,"quality-bounds.json");
                if(File.Exists(boundsFile)){
                    var profile=new JavaScriptSerializer().Deserialize<FlightBoundsProfile>(File.ReadAllText(boundsFile));
                    if(profile==null||profile.revision!=401)throw new InvalidDataException("Invalid flight bounds profile.");
                    var selected=right?profile.right:profile.left;flightOpaqueBounds=new Rectangle[4][];
                    for(int phase=0;phase<4;phase++){
                        int[][] boxes;if(selected==null||!selected.TryGetValue(FlightPhases[phase],out boxes)||boxes.Length!=FlightCounts[phase])throw new InvalidDataException("Incomplete flight bounds.");
                        flightOpaqueBounds[phase]=new Rectangle[boxes.Length];
                        for(int frame=0;frame<boxes.Length;frame++){
                            int[] b=boxes[frame];if(b.Length!=4||b[0]<0||b[1]<0||b[2]<=0||b[3]<=0||b[0]+b[2]>272||b[1]+b[3]>208)throw new InvalidDataException("Invalid flight opaque area.");
                            flightOpaqueBounds[phase][frame]=new Rectangle(b[0],b[1],b[2],b[3]);
                            flightMotionBounds=flightMotionBounds.IsEmpty?flightOpaqueBounds[phase][frame]:Rectangle.Union(flightMotionBounds,flightOpaqueBounds[phase][frame]);
                        }
                    }
                }
                flightSheetKey = identity; return true;
            } catch { ReleaseFlightArtwork(); return false; }
        }
        private bool PrepareFlightArtwork()
        {
            string outfit = FlightOutfitKey();
            if (!EnsureFlightArtwork(outfit, FlightFacingRight)) return false;
            flightOutfit = outfit; flightLandingStarted = -10000; return true;
        }
        private void FlightFrameAt(long now, out int phase, out int frame)
        {
            double elapsed = Math.Max(0, now - flightStarted);
            if (elapsed < 520) { phase = 0; frame = (int)Math.Floor(elapsed / 520 * (FlightCounts[0] - 1)); }
            else if (elapsed < flightDuration - 620) { phase = 1; frame = (int)Math.Floor((elapsed - 520) % 560 / 560 * FlightCounts[1]); }
            else { phase = 2; frame = (int)Math.Floor(Math.Max(0, elapsed - (flightDuration - 620)) / 620 * (FlightCounts[2] - 1)); }
            frame = Clamp(frame, 0, FlightCounts[phase] - 1);
        }
        private void DrawAuthoredFlightFrame(Graphics g, int phase, int frame)
        {
            if (!EnsureFlightArtwork(flightOutfit, FlightFacingRight)) return;
            var source = new Rectangle((frame % 8) * 272, (frame / 8) * 208, 272, 208);
            // Each frame is already a complete independently authored pose with
            // its own wings/tail. Only one transparent cell is painted per tick.
            g.DrawImage(flightSheets[phase], new Rectangle(0, 122, 272, 208), source, GraphicsUnit.Pixel);
        }
        private float FlightPitchAt(long now)
        {
            double elapsed = Math.Max(0, now - flightStarted); PointF point, tangent;
            FlightPathAt(elapsed / Math.Max(1, flightDuration), out point, out tangent);
            double angle = Math.Atan2(tangent.Y, Math.Max(.001, Math.Abs(tangent.X))) * 180 / Math.PI * (FlightFacingRight ? 1 : -1);
            double launch = Math.Max(0, Math.Min(1, (elapsed - 100) / 420)); launch = launch * launch * (3 - 2 * launch);
            double settle = Math.Max(0, Math.Min(1, (elapsed - (flightDuration - 620)) / 620)); settle = settle * settle * (3 - 2 * settle);
            return (float)(Math.Max(-65, Math.Min(65, angle)) * launch * (1 - settle));
        }
        private void DrawReturnFlight(Graphics g, Image fallback, long now)
        {
            GraphicsState state = g.Save(); float angle = FlightPitchAt(now);
            int phase, frame; FlightFrameAt(now, out phase, out frame);
            bool authored=EnsureFlightArtwork(flightOutfit,FlightFacingRight);
            float fit=FlightFrameFit(phase,frame,angle);
            g.TranslateTransform(136, 226); g.RotateTransform(angle); g.ScaleTransform(fit, fit); g.TranslateTransform(-136, -226);
            if (authored) DrawAuthoredFlightFrame(g, phase, frame);
            else if (fallback != null) g.DrawImage(fallback, new Rectangle(8, 122, 256, 208));
            g.Restore(state);
        }
        private float FlightFrameFit(int phase,int frame,float angle)
        {
            // Keep one envelope across the motion so wing beats cannot pulse the
            // whole character's scale. Transparent cell padding stays excluded.
            Rectangle bounds=flightOpaqueBounds==null?new Rectangle(0,0,272,208):flightMotionBounds;
            double radians=angle*Math.PI/180,c=Math.Cos(radians),s=Math.Sin(radians),maxX=1,maxY=1;
            foreach(int x in new[]{bounds.Left,bounds.Right})foreach(int y in new[]{bounds.Top,bounds.Bottom}){
                double px=x-136,py=y-104;maxX=Math.Max(maxX,Math.Abs(px*c-py*s));maxY=Math.Max(maxY,Math.Abs(px*s+py*c));
            }
            return (float)Math.Min(1,Math.Min(134/maxX,102/maxY));
        }
        private void DrawFlightLanding(Graphics g, long now)
        {
            int frame = Clamp((int)((now - flightLandingStarted) / 650F * (FlightCounts[3] - 1)), 0, FlightCounts[3] - 1);
            DrawAuthoredFlightFrame(g, 3, frame);
        }
    }
}

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace XiaoJingDesktopPet
{
    public sealed partial class PetForm
    {
        private string inputRecordingDirectory, lastRecordedState;
        private bool interactiveCheck;
        private long lastRecordedAt = -10000;
        private int recordedFrameCount;
        public void EnableInteractionRecording(string directory)
        {
            if (!diagnosticPreview) throw new InvalidOperationException("Recording requires the isolated diagnostic mode.");
            string path = Path.GetFullPath(directory);
            if (!path.StartsWith(applicationDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Recording must remain inside the diagnostic app folder.");
            Directory.CreateDirectory(path); inputRecordingDirectory = path;
            interactiveCheck = true; FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            ShowInTaskbar = true; ClientSize = ScaledSize();
        }
        private void RecordInputEvent(string name, Point point)
        {
            if (inputRecordingDirectory == null) return;
            File.AppendAllText(Path.Combine(inputRecordingDirectory, "events.csv"), animationClock.ElapsedMilliseconds + "," + name + "," + point.X + "," + point.Y + "," + Left + "," + Top + Environment.NewLine);
        }
        private void RecordInteractionFrame(Bitmap bitmap, long now)
        {
            if (inputRecordingDirectory == null || recordedFrameCount >= 300) return;
            string state = lifted ? "lifted" : falling ? "falling" : LandingAt(now) ? "landing" : PettingAt(now) ? "petting" : FeedingAt(now) ? "feeding" : StretchingAt(now) ? "stretching" : leisure != "" ? leisure : "idle";
            if (state == lastRecordedState && (state == "idle" || now - lastRecordedAt < 120)) return;
            bitmap.Save(Path.Combine(inputRecordingDirectory, "frame-" + recordedFrameCount.ToString("000") + ".png"), ImageFormat.Png);
            File.AppendAllText(Path.Combine(inputRecordingDirectory, "frames.csv"), recordedFrameCount + "," + now + "," + state + "," + ActionPoseIndex(now) + "," + Left + "," + Top + Environment.NewLine);
            recordedFrameCount++; lastRecordedAt = now; lastRecordedState = state;
        }
    }
}

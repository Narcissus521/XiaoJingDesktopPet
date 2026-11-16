using System;

namespace XiaoJingDesktopPet
{
    internal static class WorkCycleTiming
    {
        internal const int Period = 20000;
        internal static ScenePose At(long elapsed)
        {
            long t = Math.Max(0, elapsed) % Period;
            if (t < 3200) return new ScenePose(0, 2, (1 - Math.Cos(t / 3200.0 * Math.PI * 2)) * .5, "think");
            if (t < 3600) return new ScenePose(0, 1, (t - 3200) / 400.0, "think");
            if (t < 4000) return new ScenePose(1, 2, (t - 3600) / 400.0, "think");
            if (t < 4500) return new ScenePose(2, 3, (t - 4000) / 500.0, "reach");
            if (t < 5000) return new ScenePose(3, 4, (t - 4500) / 500.0, "reach");
            if (t < 12600) {
                int[] keys = { 4, 5, 6, 7, 6, 5, 4 };
                // Seven full alternating phrases and a final lift end exactly
                // on pose 5, which is the start of the pause transition.
                double step = (t - 5000) * 43.0 / 7600.0; int segment = (int)step % 6;
                return new ScenePose(keys[segment], keys[segment + 1], step - Math.Floor(step), "type");
            }
            if (t < 13200) return new ScenePose(5, 8, (t - 12600) / 600.0, "pause");
            if (t < 13800) return new ScenePose(8, 9, (t - 13200) / 600.0, "lift");
            if (t < 14400) return new ScenePose(9, 10, (t - 13800) / 600.0, "lift");
            if (t < 18600) return new ScenePose(10, 0, (t - 14400) / 4200.0, "think");
            if (t < 19100) return new ScenePose(0, 11, (t - 18600) / 500.0, "think");
            return new ScenePose(11, 0, (t - 19100) / 900.0, "think");
        }
    }
}

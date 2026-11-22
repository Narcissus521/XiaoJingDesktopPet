using System.Collections.Generic;
using System.Drawing;

namespace XiaoJingDesktopPet {
    internal static class TeaCupLandmarks {
        // Cup bowl centres in tea keys 04 through 10, in sprite coordinates.
        private static readonly Dictionary<string, PointF[]> Centres = new Dictionary<string, PointF[]> {
            {"base", new PointF[] { new PointF(131, 82), new PointF(132, 82), new PointF(132, 82), new PointF(132, 92), new PointF(132, 92), new PointF(132, 101), new PointF(133, 102) }},
            {"chongyang", new PointF[] { new PointF(132, 83), new PointF(133, 83), new PointF(132, 83), new PointF(132, 92), new PointF(132, 92), new PointF(133, 102), new PointF(135, 103) }},
            {"christmas", new PointF[] { new PointF(132, 85), new PointF(133, 85), new PointF(133, 84), new PointF(132, 101), new PointF(132, 101), new PointF(133, 109), new PointF(134, 109) }},
            {"dragonboat", new PointF[] { new PointF(133, 84), new PointF(134, 84), new PointF(133, 83), new PointF(134, 101), new PointF(134, 101), new PointF(134, 108), new PointF(134, 109) }},
            {"halloween", new PointF[] { new PointF(132, 88), new PointF(133, 88), new PointF(132, 88), new PointF(133, 102), new PointF(133, 102), new PointF(132, 112), new PointF(132, 113) }},
            {"laba", new PointF[] { new PointF(133, 87), new PointF(134, 87), new PointF(133, 87), new PointF(134, 97), new PointF(134, 97), new PointF(137, 113), new PointF(137, 113) }},
            {"lantern", new PointF[] { new PointF(135, 86), new PointF(136, 86), new PointF(134, 85), new PointF(134, 98), new PointF(134, 98), new PointF(136, 108), new PointF(137, 109) }},
            {"midautumn", new PointF[] { new PointF(135, 84), new PointF(136, 84), new PointF(135, 85), new PointF(136, 94), new PointF(136, 94), new PointF(136, 105), new PointF(136, 106) }},
            {"qingming", new PointF[] { new PointF(137, 82), new PointF(138, 82), new PointF(137, 83), new PointF(138, 97), new PointF(138, 97), new PointF(137, 105), new PointF(138, 107) }},
            {"qixi", new PointF[] { new PointF(132, 84), new PointF(133, 84), new PointF(132, 84), new PointF(132, 94), new PointF(132, 94), new PointF(132, 104), new PointF(133, 105) }},
            {"rain-2", new PointF[] { new PointF(133, 88), new PointF(134, 88), new PointF(133, 87), new PointF(133, 101), new PointF(133, 101), new PointF(134, 112), new PointF(134, 113) }},
            {"rain-3", new PointF[] { new PointF(133, 91), new PointF(134, 91), new PointF(133, 91), new PointF(133, 105), new PointF(133, 105), new PointF(136, 116), new PointF(136, 116) }},
            {"snow-1", new PointF[] { new PointF(133, 83), new PointF(134, 83), new PointF(133, 83), new PointF(135, 98), new PointF(135, 98), new PointF(134, 104), new PointF(135, 105) }},
            {"snow-2", new PointF[] { new PointF(134, 85), new PointF(135, 85), new PointF(133, 83), new PointF(135, 101), new PointF(135, 101), new PointF(136, 111), new PointF(137, 112) }},
            {"snow-3", new PointF[] { new PointF(135, 90), new PointF(136, 90), new PointF(135, 88), new PointF(137, 106), new PointF(137, 106), new PointF(137, 115), new PointF(137, 116) }},
            {"springfestival", new PointF[] { new PointF(132, 83), new PointF(133, 83), new PointF(132, 80), new PointF(133, 95), new PointF(133, 95), new PointF(133, 105), new PointF(133, 107) }},
            {"sun-2", new PointF[] { new PointF(133, 85), new PointF(134, 85), new PointF(134, 85), new PointF(133, 97), new PointF(133, 97), new PointF(134, 106), new PointF(134, 107) }},
            {"sun-3", new PointF[] { new PointF(138, 85), new PointF(139, 85), new PointF(138, 85), new PointF(139, 95), new PointF(139, 95), new PointF(137, 107), new PointF(137, 108) }},
            {"term-bailu", new PointF[] { new PointF(133, 82), new PointF(134, 82), new PointF(133, 82), new PointF(133, 92), new PointF(133, 92), new PointF(133, 101), new PointF(134, 102) }},
            {"term-chunfen", new PointF[] { new PointF(133, 83), new PointF(134, 83), new PointF(134, 84), new PointF(134, 94), new PointF(134, 94), new PointF(135, 104), new PointF(134, 106) }},
            {"term-chushu", new PointF[] { new PointF(134, 83), new PointF(135, 83), new PointF(133, 82), new PointF(134, 92), new PointF(134, 92), new PointF(134, 102), new PointF(135, 104) }},
            {"term-dahan", new PointF[] { new PointF(133, 84), new PointF(134, 84), new PointF(133, 84), new PointF(134, 96), new PointF(134, 96), new PointF(133, 109), new PointF(134, 110) }},
            {"term-dashu", new PointF[] { new PointF(133, 85), new PointF(134, 85), new PointF(133, 86), new PointF(135, 94), new PointF(135, 94), new PointF(134, 104), new PointF(135, 104) }},
            {"term-daxue", new PointF[] { new PointF(130, 85), new PointF(131, 85), new PointF(130, 84), new PointF(130, 98), new PointF(130, 98), new PointF(134, 109), new PointF(135, 112) }},
            {"term-dongzhi", new PointF[] { new PointF(130, 85), new PointF(131, 85), new PointF(130, 85), new PointF(131, 99), new PointF(131, 99), new PointF(132, 111), new PointF(132, 112) }},
            {"term-guyu", new PointF[] { new PointF(132, 84), new PointF(133, 84), new PointF(131, 84), new PointF(133, 92), new PointF(133, 92), new PointF(133, 105), new PointF(133, 105) }},
            {"term-hanlu", new PointF[] { new PointF(134, 83), new PointF(135, 83), new PointF(134, 84), new PointF(133, 95), new PointF(133, 95), new PointF(134, 102), new PointF(135, 103) }},
            {"term-jingzhe", new PointF[] { new PointF(134, 83), new PointF(135, 83), new PointF(135, 83), new PointF(135, 91), new PointF(135, 91), new PointF(135, 101), new PointF(135, 102) }},
            {"term-lichun", new PointF[] { new PointF(130, 84), new PointF(131, 83), new PointF(130, 84), new PointF(131, 93), new PointF(131, 93), new PointF(132, 102), new PointF(132, 103) }},
            {"term-lidong", new PointF[] { new PointF(133, 86), new PointF(134, 86), new PointF(133, 86), new PointF(134, 99), new PointF(134, 99), new PointF(133, 111), new PointF(134, 113) }},
            {"term-liqiu", new PointF[] { new PointF(133, 84), new PointF(133, 84), new PointF(133, 84), new PointF(136, 96), new PointF(136, 96), new PointF(134, 103), new PointF(135, 104) }},
            {"term-lixia", new PointF[] { new PointF(132, 83), new PointF(133, 83), new PointF(133, 82), new PointF(132, 94), new PointF(132, 94), new PointF(133, 101), new PointF(133, 103) }},
            {"term-mangzhong", new PointF[] { new PointF(132, 82), new PointF(133, 82), new PointF(132, 82), new PointF(132, 93), new PointF(132, 93), new PointF(132, 101), new PointF(132, 102) }},
            {"term-qingming", new PointF[] { new PointF(132, 82), new PointF(133, 82), new PointF(133, 81), new PointF(133, 93), new PointF(133, 93), new PointF(132, 102), new PointF(132, 106) }},
            {"term-qiufen", new PointF[] { new PointF(134, 81), new PointF(135, 81), new PointF(133, 82), new PointF(134, 91), new PointF(134, 91), new PointF(134, 101), new PointF(135, 103) }},
            {"term-shuangjiang", new PointF[] { new PointF(133, 83), new PointF(134, 83), new PointF(133, 83), new PointF(133, 92), new PointF(133, 92), new PointF(134, 101), new PointF(134, 103) }},
            {"term-xiaohan", new PointF[] { new PointF(131, 84), new PointF(132, 84), new PointF(132, 83), new PointF(131, 101), new PointF(131, 101), new PointF(130, 107), new PointF(132, 108) }},
            {"term-xiaoman", new PointF[] { new PointF(134, 86), new PointF(135, 86), new PointF(134, 84), new PointF(134, 95), new PointF(134, 95), new PointF(134, 104), new PointF(134, 105) }},
            {"term-xiaoshu", new PointF[] { new PointF(132, 83), new PointF(133, 83), new PointF(132, 83), new PointF(132, 93), new PointF(132, 93), new PointF(135, 104), new PointF(136, 105) }},
            {"term-xiaoxue", new PointF[] { new PointF(132, 83), new PointF(133, 83), new PointF(133, 83), new PointF(133, 93), new PointF(133, 93), new PointF(134, 104), new PointF(135, 105) }},
            {"term-xiazhi", new PointF[] { new PointF(134, 82), new PointF(135, 82), new PointF(134, 83), new PointF(133, 93), new PointF(133, 93), new PointF(133, 102), new PointF(134, 103) }},
            {"term-yushui", new PointF[] { new PointF(133, 84), new PointF(134, 84), new PointF(133, 84), new PointF(132, 93), new PointF(132, 93), new PointF(133, 104), new PointF(133, 105) }},
        };

        internal static PointF[] For(string outfit) {
            PointF[] points;
            return Centres.TryGetValue(outfit, out points) ? points : null;
        }
    }
}

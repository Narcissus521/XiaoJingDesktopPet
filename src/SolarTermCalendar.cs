using System;
using System.Collections.Generic;

namespace XiaoJingDesktopPet
{
    public static class SolarTermCalendar
    {
        private static readonly string[] TermIds = { "lichun", "yushui", "jingzhe", "chunfen", "qingming", "guyu", "lixia", "xiaoman", "mangzhong", "xiazhi", "xiaoshu", "dashu", "liqiu", "chushu", "bailu", "qiufen", "hanlu", "shuangjiang", "lidong", "xiaoxue", "daxue", "dongzhi", "xiaohan", "dahan" };
        private static readonly string[] Names = { "立春", "雨水", "惊蛰", "春分", "清明", "谷雨", "立夏", "小满", "芒种", "夏至", "小暑", "大暑", "立秋", "处暑", "白露", "秋分", "寒露", "霜降", "立冬", "小雪", "大雪", "冬至", "小寒", "大寒" };
        private static readonly string[] Foods = { "春饼", "荠菜馄饨", "梨", "春菜饼", "青团", "香椿拌豆腐", "立夏蛋", "苦菜饼", "青梅糕", "凉面", "藕粉羹", "绿豆汤", "西瓜", "百合粥", "桂圆银耳羹", "芝麻汤圆", "芝麻糕", "柿子", "饺子", "糍粑", "烤红薯", "汤圆", "糯米饭", "八宝饭" };
        private static readonly string[] Cuffs = { "99,156,134", "109,153,187", "119,162,105", "183,126,152", "103,151,133", "101,144,92", "89,155,155", "180,155,91", "124,158,82", "179,132,164", "114,165,132", "103,159,131", "176,137,91", "146,135,176", "133,162,187", "187,148,85", "145,119,160", "186,111,77", "151,75,99", "124,162,190", "83,113,164", "171,80,100", "157,127,163", "110,102,157" };
        private static readonly Dictionary<int, int[]> PublishedDays = new Dictionary<int, int[]> {
            { 2026, new[] { 4, 18, 5, 20, 5, 20, 5, 21, 5, 21, 7, 23, 7, 23, 7, 23, 8, 23, 7, 22, 7, 22, 5, 20 } },
            { 2027, new[] { 4, 19, 6, 21, 5, 20, 6, 21, 6, 21, 7, 23, 8, 23, 8, 23, 8, 23, 7, 22, 7, 22, 5, 20 } },
            { 2028, new[] { 4, 19, 5, 20, 4, 19, 5, 20, 5, 21, 6, 22, 7, 22, 7, 22, 8, 23, 7, 22, 6, 21, 6, 20 } },
        };
        private static readonly Dictionary<int, DateTime[]> Cache = new Dictionary<int, DateTime[]>();
        private static readonly object Gate = new object();
        public static string[] Ids { get { return (string[])TermIds.Clone(); } }
        public static bool IsValidId(string id) { return Array.IndexOf(TermIds, id) >= 0; }
        public static bool IsTermOutfit(string id) { return id != null && id.StartsWith("term-", StringComparison.Ordinal) && IsValidId(id.Substring(5)); }
        private static int Index(string id) {
            string term = IsTermOutfit(id) ? id.Substring(5) : id;
            int index = Array.IndexOf(TermIds, term);
            if (index < 0) throw new ArgumentOutOfRangeException("id");
            return index;
        }
        public static string GetName(string id) { return Names[Index(id)]; }
        public static string GetFoodName(string id) { return Foods[Index(id)]; }
        public static int[] GetCuffRgb(string id) {
            string[] parts = Cuffs[Index(id)].Split(',');
            return new[] { Int32.Parse(parts[0]), Int32.Parse(parts[1]), Int32.Parse(parts[2]) };
        }
        public static string GetSolarTerm(DateTime shanghaiDate) {
            if (shanghaiDate.Year < 1901 || shanghaiDate.Year > 2100) return null;
            DateTime[] dates = Dates(shanghaiDate.Year);
            for (int i = 0; i < dates.Length; i++) if (dates[i] == shanghaiDate.Date) return TermIds[i];
            return null;
        }
        public static DateTime GetDate(int year, string id) {
            if (year < 1901 || year > 2100) throw new ArgumentOutOfRangeException("year");
            return Dates(year)[Index(id)];
        }
        // Manual previews override automatic dates. For automatic dressing,
        // traditional holidays have priority over solar terms, then weather.
        public static string ResolveOutfit(DateTime shanghaiDate, string holidayMode, string termMode) {
            if (holidayMode != "auto" && holidayMode != "off") return holidayMode;
            if (IsValidId(termMode)) return "term-" + termMode;
            string holiday = holidayMode == "auto" ? HolidayCalendar.GetHoliday(shanghaiDate) : null;
            if (holiday != null) return holiday;
            string term = termMode == "auto" ? GetSolarTerm(shanghaiDate) : null;
            return term == null ? null : "term-" + term;
        }
        private static DateTime[] Dates(int year) {
            lock (Gate) {
                DateTime[] dates;
                if (Cache.TryGetValue(year, out dates)) return dates;
                int[] published;
                PublishedDays.TryGetValue(year, out published);
                dates = new DateTime[24];
                for (int i = 0; i < 24; i++) {
                    int month = (i / 2 + 1) % 12 + 1;
                    if (published != null) { dates[i] = new DateTime(year, month, published[i]); continue; }
                    int day = i % 2 == 0 ? (month < 7 ? 5 : 7) : (month < 7 ? 20 : 22);
                    DateTime lower = new DateTime(year, month, day - 3, 0, 0, 0, DateTimeKind.Utc), upper = lower.AddDays(6);
                    double target = (315 + 15 * i) % 360;
                    for (int step = 0; step < 40; step++) {
                        DateTime middle = lower.AddTicks((upper.Ticks - lower.Ticks) / 2);
                        double delta = (HolidayCalendar.SolarLongitude(middle) - target + 540) % 360 - 180;
                        if (delta < 0) lower = middle; else upper = middle;
                    }
                    dates[i] = DateTime.SpecifyKind(upper.AddHours(8).Date, DateTimeKind.Unspecified);
                }
                Cache.Add(year, dates); return dates;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace XiaoJingDesktopPet
{
    public static class HolidayCalendar
    {
        private static readonly ChineseLunisolarCalendar Lunar = new ChineseLunisolarCalendar();
        private static readonly Dictionary<int, DateTime> QingmingDates = new Dictionary<int, DateTime>();
        private static readonly object CalendarLock = new object();

        // The caller passes the civil date in Asia/Shanghai, not the host timezone.
        public static string GetHoliday(DateTime shanghaiDate)
        {
            DateTime date = shanghaiDate.Date;
            if (date.Month == 12 && date.Day >= 24 && date.Day <= 26) return "christmas";
            if (date.Month == 10 && date.Day == 31) return "halloween";
            lock (CalendarLock)
            {
                if (date.Year >= 1901 && date.Year <= 2100 && date.Month == 4 && date == QingmingDate(date.Year)) return "qingming";
                if (date < Lunar.MinSupportedDateTime.Date || date > Lunar.MaxSupportedDateTime.Date) return null;
                int year = Lunar.GetYear(date);
                int month = Lunar.GetMonth(date);
                int day = Lunar.GetDayOfMonth(date);
                int leap = Lunar.GetLeapMonth(year);
                // A leap month is numbered separately; do not celebrate a festival twice.
                if (leap != 0 && month == leap) return null;
                if (leap != 0 && month > leap) month--;
                if (month == 1 && day <= 3) return "springfestival";
                if (month == 12 && date < Lunar.MaxSupportedDateTime.Date && Lunar.GetYear(date.AddDays(1)) != year) return "springfestival";
                if (month == 1 && day == 15) return "lantern";
                if (month == 5 && day == 5) return "dragonboat";
                if (month == 7 && day == 7) return "qixi";
                if (month == 8 && day == 15) return "midautumn";
                if (month == 9 && day == 9) return "chongyang";
                if (month == 12 && day == 8) return "laba";
                return null;
            }
        }

        public static string GetName(string id)
        {
            if (SolarTermCalendar.IsTermOutfit(id)) return SolarTermCalendar.GetName(id) + "节气";
            switch (id)
            {
                case "springfestival": return "春节 / 除夕";
                case "lantern": return "元宵节";
                case "qingming": return "清明节";
                case "dragonboat": return "端午节";
                case "qixi": return "七夕节";
                case "midautumn": return "中秋节";
                case "chongyang": return "重阳节";
                case "laba": return "腊八节";
                case "halloween": return "万圣节";
                case "christmas": return "圣诞节";
                default: return "日常天气装";
            }
        }

        // Qingming is the solar term at apparent solar longitude 15 degrees.
        // Meeus/NOAA solar-position equations; solve the instant then use UTC+8.
        // Verified against Hong Kong Observatory published calendars for 2026-2028.
        public static DateTime QingmingDate(int year)
        {
            if (year < 1901 || year > 2100) throw new ArgumentOutOfRangeException("year");
            lock (CalendarLock)
            {
                DateTime known;
                if (QingmingDates.TryGetValue(year, out known)) return known;
                DateTime lower = new DateTime(year, 4, 2, 0, 0, 0, DateTimeKind.Utc);
                DateTime upper = new DateTime(year, 4, 7, 0, 0, 0, DateTimeKind.Utc);
                for (int i = 0; i < 40; i++)
                {
                    DateTime middle = lower.AddTicks((upper.Ticks - lower.Ticks) / 2);
                    if (SolarLongitude(middle) < 15) lower = middle;
                    else upper = middle;
                }
                DateTime result = DateTime.SpecifyKind(upper.AddHours(8).Date, DateTimeKind.Unspecified);
                QingmingDates[year] = result;
                return result;
            }
        }

        internal static double SolarLongitude(DateTime utc)
        {
            double t = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays / 36525.0;
            double meanLongitude = 280.46646 + t * (36000.76983 + 0.0003032 * t);
            double anomaly = 357.52911 + t * (35999.05029 - 0.0001537 * t);
            double center = Sin(anomaly) * (1.914602 - t * (0.004817 + 0.000014 * t))
                + Sin(2 * anomaly) * (0.019993 - 0.000101 * t) + Sin(3 * anomaly) * 0.000289;
            double apparent = meanLongitude + center - 0.00569 - 0.00478 * Sin(125.04 - 1934.136 * t);
            return (apparent % 360 + 360) % 360;
        }
        private static double Sin(double degrees) { return Math.Sin(degrees * Math.PI / 180.0); }
    }
}

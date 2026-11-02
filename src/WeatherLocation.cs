using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace XiaoJingDesktopPet
{
    public sealed class WeatherLocationData
    {
        public string Name { get; set; }
        public string Admin1 { get; set; }
        public string Admin2 { get; set; }
        public string Country { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public static WeatherLocationData DefaultZhuji
        { get { return new WeatherLocationData { Name = "诸暨", Admin1 = "浙江", Admin2 = "绍兴", Country = "中国", Latitude = 29.71877, Longitude = 120.24233 }; } }
        public string Region { get { return JoinDistinct(Admin2, Admin1); } }
        public string DisplayName { get { return JoinDistinct(Name, Admin2, Admin1, Country); } }
        public string CoordinatesText
        { get { return Latitude.ToString("0.00000", CultureInfo.InvariantCulture) + ", " + Longitude.ToString("0.00000", CultureInfo.InvariantCulture); } }
        public WeatherLocationData Copy()
        { return new WeatherLocationData { Name = Name, Admin1 = Admin1, Admin2 = Admin2, Country = Country, Latitude = Latitude, Longitude = Longitude }; }
        public WeatherLocationData ValidatedCopy()
        {
            if (Double.IsNaN(Latitude) || Double.IsInfinity(Latitude) || Latitude < -90 || Latitude > 90
                || Double.IsNaN(Longitude) || Double.IsInfinity(Longitude) || Longitude < -180 || Longitude > 180)
                throw new ArgumentException("地点经纬度无效。", "location");
            var value = Copy();
            value.Name = Clean(Name); value.Admin1 = Clean(Admin1); value.Admin2 = Clean(Admin2); value.Country = Clean(Country);
            if (String.IsNullOrWhiteSpace(value.Name)) throw new ArgumentException("地点名称不能为空。", "location");
            return value;
        }
        private static string Clean(string value)
        {
            value = (value ?? "").Trim();
            if (value.Length > 150) throw new ArgumentException("地点名称过长。", "location");
            foreach (char c in value) if (Char.IsControl(c)) throw new ArgumentException("地点名称包含无效字符。", "location");
            return value;
        }
        private static string JoinDistinct(params string[] values)
        {
            var parts = new List<string>();
            foreach (string value in values) if (!String.IsNullOrWhiteSpace(value) && !parts.Contains(value)) parts.Add(value);
            return String.Join(" · ", parts);
        }
    }

    public static class WeatherLocationSearch
    {
        public static string BuildSearchUrl(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2 || query.Length > 120) throw new ArgumentException("请输入 2–120 个字的城市名或邮编。", "query");
            return "https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query) + "&count=20&language=zh&format=json";
        }
        public static Task<List<WeatherLocationData>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            string url = BuildSearchUrl(query);
            return Task.Run(delegate {
                cancellationToken.ThrowIfCancellationRequested();
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 12000; request.ReadWriteTimeout = 12000;
                request.UserAgent = "XiaoJingDesktopPet/3.1 (location search)";
                request.Headers[HttpRequestHeader.AcceptEncoding] = "identity";
                try {
                    using (cancellationToken.Register(request.Abort))
                    using (var response = request.GetResponse())
                    using (var stream = response.GetResponseStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                        var buffer = new char[4096]; var json = new StringBuilder(); int count;
                        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (json.Length + count > 1048576) throw new InvalidDataException("地点搜索响应过大。");
                            json.Append(buffer, 0, count);
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        return ParseResults(json.ToString());
                    }
                } catch (WebException) { cancellationToken.ThrowIfCancellationRequested(); throw; }
            }, cancellationToken);
        }
        public static List<WeatherLocationData> ParseResults(string json)
        {
            if (json == null || json.Length > 1048576) throw new InvalidDataException("地点搜索响应无效。");
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576, RecursionLimit = 32 };
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("地点搜索响应无效。");
            object value;
            if (root.TryGetValue("error", out value) && value is bool && (bool)value) throw new InvalidDataException("地点搜索服务暂不可用。");
            var result = new List<WeatherLocationData>();
            if (!root.TryGetValue("results", out value)) return result;
            var rows = value as IEnumerable;
            if (rows == null || value is string || value is Dictionary<string, object>) throw new InvalidDataException("地点列表无效。");
            foreach (object item in rows) {
                var row = item as Dictionary<string, object>;
                double latitude, longitude;
                if (row == null || !Number(row, "latitude", out latitude) || !Number(row, "longitude", out longitude)) continue;
                var place = new WeatherLocationData { Name = Text(row, "name"), Admin1 = Text(row, "admin1"), Admin2 = Text(row, "admin2"),
                    Country = Text(row, "country"), Latitude = latitude, Longitude = longitude };
                if (String.IsNullOrWhiteSpace(place.Country)) place.Country = Text(row, "country_code");
                try { result.Add(place.ValidatedCopy()); } catch (ArgumentException) { continue; }
                if (result.Count == 20) break;
            }
            return result;
        }
        private static string Text(Dictionary<string, object> value, string key)
        { object item; return value.TryGetValue(key, out item) ? item as string : null; }
        private static bool Number(Dictionary<string, object> value, string key, out double number)
        { object item; number = 0; return value.TryGetValue(key, out item) && Double.TryParse(Convert.ToString(item, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number); }
    }
}

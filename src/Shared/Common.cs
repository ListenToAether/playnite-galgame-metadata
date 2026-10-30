using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameMetadata.Shared
{
    /// <summary>Raised when a site rejects the request or returns unusable data.</summary>
    public class ScrapeException : Exception
    {
        public ScrapeException(string message) : base(message) { }
        public ScrapeException(string message, Exception inner) : base(message, inner) { }
    }

    public class ScrapedLink
    {
        public string Name { get; set; }
        public string Url { get; set; }

        public ScrapedLink() { }
        public ScrapedLink(string name, string url) { Name = name; Url = url; }
    }

    /// <summary>
    /// Site independent scraping result. The Playnite provider projects this into SDK models;
    /// keeping it free of Playnite types lets the same code run in the console test harness.
    /// </summary>
    public class ScrapedGame
    {
        public string SourceName { get; set; }      // e.g. "Ymgal"
        public string SourceId { get; set; }        // native id used by the site
        public string SiteUrl { get; set; }         // human facing page, stored as a Playnite link

        public string Name { get; set; }            // display title chosen according to settings
        public string OriginalName { get; set; }    // original (usually Japanese) title
        public string ChineseName { get; set; }     // Chinese localized title, if the site has one
        public List<string> Aliases { get; set; }

        public string Description { get; set; }     // plain text, paragraphs separated by blank lines
        public List<string> Developers { get; set; }
        public List<string> Publishers { get; set; }
        public List<string> Tags { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Platforms { get; set; }
        public List<string> Features { get; set; }

        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }

        public string CoverUrl { get; set; }
        public string BannerUrl { get; set; }
        public List<string> Screenshots { get; set; }

        public int? CommunityScore { get; set; }    // 0-100, Playnite scale
        public double? RawScore { get; set; }       // site native score, kept for debugging
        public bool Nsfw { get; set; }
        public string AgeRating { get; set; }       // "18+" / "All ages" / null
        public string Series { get; set; }

        public List<ScrapedLink> Links { get; set; }
        public List<ScrapedLink> AssociatedSourceLinks { get; set; }

        /// <summary>
        /// Adds a link unless the url is already present. Every link the site offers is kept;
        /// duplicate urls are dropped so the same page is not listed twice.
        /// </summary>
        public void AddLink(string name, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            foreach (var existing in Links)
            {
                if (string.Equals(existing.Url, url, StringComparison.OrdinalIgnoreCase)) return;
            }
            Links.Add(new ScrapedLink(name, url));
        }

        public ScrapedGame()
        {
            Aliases = new List<string>();
            Developers = new List<string>();
            Publishers = new List<string>();
            Tags = new List<string>();
            Genres = new List<string>();
            Platforms = new List<string>();
            Features = new List<string>();
            Screenshots = new List<string>();
            Links = new List<ScrapedLink>();
            AssociatedSourceLinks = new List<ScrapedLink>();
        }
    }

    /// <summary>Per host request gate so we never hammer a site (their fair use limits).</summary>
    public static class Throttle
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, DateTime> LastCall = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static async Task WaitAsync(Uri uri, int minIntervalMs, CancellationToken cancel)
        {
            if (minIntervalMs <= 0) return;
            DateTime due;
            lock (Sync)
            {
                var host = uri.Host;
                DateTime last;
                if (!LastCall.TryGetValue(host, out last)) last = DateTime.MinValue;
                due = last.AddMilliseconds(minIntervalMs);
                // Reserve the slot immediately so concurrent callers queue up properly.
                LastCall[host] = DateTime.UtcNow > due ? DateTime.UtcNow : due;
            }
            var delay = due - DateTime.UtcNow;
            if (delay.TotalMilliseconds > 0) await Task.Delay(delay, cancel).ConfigureAwait(false);
        }
    }

    /// <summary>Result of an HTTP GET where error bodies are still interesting.</summary>
    public class HttpResult
    {
        public int StatusCode { get; set; }
        public bool IsSuccess { get; set; }
        public string Body { get; set; }
    }

    /// <summary>Small HTTP client shared by all providers.</summary>
    public static class Http
    {
        public const string DefaultUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        private static readonly HttpClient Client = CreateClient();
        private static readonly object InitSync = new object();
        private static bool _tlsConfigured;

        private static HttpClient CreateClient()
        {
            // Playnite runs on .NET Framework 4.6.2+, where TLS 1.2 must be opted into.
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,ja;q=0.8,en;q=0.7");
            return client;
        }

        public static void EnsureTls()
        {
            lock (InitSync)
            {
                if (_tlsConfigured) return;
                _tlsConfigured = true;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
                }
                catch { }
            }
        }

        public static async Task<string> GetStringAsync(
            string url,
            IDictionary<string, string> headers = null,
            int minIntervalMs = 0,
            CancellationToken cancel = default(CancellationToken))
        {
            EnsureTls();
            var uri = new Uri(url);
            await Throttle.WaitAsync(uri, minIntervalMs, cancel).ConfigureAwait(false);

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new ScrapeException(string.Format(
                            "{0} returned HTTP {1} ({2}){3}",
                            uri.Host,
                            (int)response.StatusCode,
                            response.ReasonPhrase,
                            Snippet(body)));
                    }
                    return body;
                }
            }
        }

        public static async Task<JsonValue> GetJsonAsync(
            string url,
            IDictionary<string, string> headers = null,
            int minIntervalMs = 0,
            CancellationToken cancel = default(CancellationToken))
        {
            var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Accept", "application/json" }
            };
            if (headers != null)
                foreach (var kv in headers) h[kv.Key] = kv.Value;

            var body = await GetStringAsync(url, h, minIntervalMs, cancel).ConfigureAwait(false);
            JsonValue json;
            if (!JsonValue.TryParse(body, out json) || json == null || json.IsNull)
                throw new ScrapeException("Response was not valid JSON: " + Snippet(body));
            return json;
        }

        public static async Task<byte[]> GetBytesAsync(
            string url,
            IDictionary<string, string> headers = null,
            int minIntervalMs = 0,
            CancellationToken cancel = default(CancellationToken))
        {
            EnsureTls();
            var uri = new Uri(url);
            await Throttle.WaitAsync(uri, minIntervalMs, cancel).ConfigureAwait(false);
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new ScrapeException(string.Format("{0} image request failed: HTTP {1}", uri.Host, (int)response.StatusCode));
                    return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>GET that also returns error bodies, since some APIs explain the failure in JSON.</summary>
        public static async Task<HttpResult> GetAsync(
            string url,
            IDictionary<string, string> headers = null,
            int minIntervalMs = 0,
            CancellationToken cancel = default(CancellationToken))
        {
            EnsureTls();
            var uri = new Uri(url);
            await Throttle.WaitAsync(uri, minIntervalMs, cancel).ConfigureAwait(false);
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                if (headers != null)
                {
                    foreach (var kv in headers)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    }
                }
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new HttpResult
                    {
                        StatusCode = (int)response.StatusCode,
                        IsSuccess = response.IsSuccessStatusCode,
                        Body = body
                    };
                }
            }
        }

        private static string Snippet(string body)        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            var text = body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length > 200) text = text.Substring(0, 200) + "...";
            return text.Length == 0 ? string.Empty : ": " + text;
        }
    }

    /// <summary>Title normalisation and fuzzy matching used to pick a search hit.</summary>
    public static class Text
    {
        private static readonly char[] StripChars = new[]
        {
            ' ', '\t', '\r', '\n', '\u3000', '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+', ',', '-', '.', '/',
            ':', ';', '<', '=', '>', '?', '@', '[', '\\', ']', '^', '_', '`', '{', '|', '}', '~',
            '\u00b7', '\u2022', '\u2026', '\u2018', '\u2019', '\u201c', '\u201d', '\u2013', '\u2014',
            '\uff01', '\uff02', '\uff03', '\uff04', '\uff05', '\uff06', '\uff07', '\uff08', '\uff09', '\uff0a',
            '\uff0b', '\uff0c', '\uff0d', '\uff0e', '\uff0f', '\uff1a', '\uff1b', '\uff1c', '\uff1d', '\uff1e',
            '\uff1f', '\uff20', '\uff3b', '\uff3c', '\uff3d', '\uff3e', '\uff3f', '\uff40', '\uff5b', '\uff5c',
            '\uff5d', '\uff5e', '\u3001', '\u3002', '\u300a', '\u300b', '\u3008', '\u3009', '\u3010', '\u3011',
            '\u3014', '\u3015', '\uff5f', '\uff60'
        };

        private static readonly Dictionary<char, char> WidthMap = new Dictionary<char, char>();

        static Text()
        {
            for (char c = '\uff01'; c <= '\uff5e'; c++)
                WidthMap[c] = (char)(c - 0xFEE0);
            WidthMap['\u3000'] = ' ';
        }

        /// <summary>Lowercase, width folded, punctuation and whitespace removed.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length);
            foreach (var raw in value)
            {
                char c = raw;
                char mapped;
                if (WidthMap.TryGetValue(c, out mapped)) c = mapped;
                if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
                if (Array.IndexOf(StripChars, c) >= 0) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Dice coefficient over character bigrams, 0..1.</summary>
        public static double Similarity(string a, string b)
        {
            var x = Normalize(a);
            var y = Normalize(b);
            if (x.Length == 0 || y.Length == 0) return 0;
            if (x == y) return 1.0;

            var grams = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < x.Length; i++)
            {
                var g = x.Substring(i, 2);
                int n;
                grams.TryGetValue(g, out n);
                grams[g] = n + 1;
            }
            int hits = 0, total = 0;
            for (int i = 0; i + 1 < y.Length; i++)
            {
                var g = y.Substring(i, 2);
                int n;
                if (grams.TryGetValue(g, out n) && n > 0)
                {
                    grams[g] = n - 1;
                    hits++;
                }
                total++;
            }
            if (total == 0) return 0;
            double dice = (2.0 * hits) / (grams.Values.Sum() + hits + (total - hits));
            // Containment helps short queries inside long titles.
            if (x.IndexOf(y, StringComparison.Ordinal) >= 0 || y.IndexOf(x, StringComparison.Ordinal) >= 0)
                dice = Math.Max(dice, 0.85);
            return Math.Min(1.0, dice);
        }

        /// <summary>Picks the best matching item, or -1 when nothing is similar enough.</summary>
        public static int BestIndex<T>(IList<T> items, Func<T, IEnumerable<string>> names, string query, double threshold = 0.45)
        {
            if (items == null || items.Count == 0) return -1;
            int best = -1;
            double bestScore = 0;
            for (int i = 0; i < items.Count; i++)
            {
                double score = 0;
                foreach (var name in names(items[i]))
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    score = Math.Max(score, Similarity(name, query));
                }
                // Small preference for earlier hits, sites order by relevance.
                score -= i * 0.01;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return bestScore >= threshold ? best : -1;
        }

        public static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var text = html;
            // Convert common block tags to newlines before removing markup.
            text = System.Text.RegularExpressions.Regex.Replace(text, "(?i)<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.None);
            text = System.Text.RegularExpressions.Regex.Replace(text, "(?i)</p\\s*>", "\n\n", System.Text.RegularExpressions.RegexOptions.None);
            text = System.Text.RegularExpressions.Regex.Replace(text, "(?i)<li[^>]*>", "\n- ", System.Text.RegularExpressions.RegexOptions.None);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty, System.Text.RegularExpressions.RegexOptions.None);
            text = WebUtility.HtmlDecode(text);
            return CleanDescription(text);
        }

        /// <summary>Removes BBCode and Markdown link syntax while keeping readable text.</summary>
        public static string StripMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var value = text;
            value = Regex.Replace(value, @"\[url=[^\]]*\]", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\[/url\]", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\[/?(?:b|i|u|s|quote|code|spoiler|spoilerbox|size|color|font|center|left|right|list|\*|img)[^\]\[]*\]",
                string.Empty, RegexOptions.IgnoreCase);
            // [label](url) -> label
            value = Regex.Replace(value, @"\[([^\]]+)\]\([^)]*\)", "$1");
            // emphasis markers left behind by markdown
            value = Regex.Replace(value, @"\*\*|__|\\\r?\n", string.Empty);
            // lines that only contain a bare backslash used as a spacer
            value = Regex.Replace(value, @"(?m)^[ \t]*\\[ \t]*$", string.Empty);
            return value;
        }

        /// <summary>Normalises newlines, trims trailing spaces and collapses blank line runs.</summary>
        public static string CleanDescription(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var sb = new StringBuilder();
            int blanks = 0;
            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                if (line.Trim().Length == 0)
                {
                    blanks++;
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append('\n');
                    if (blanks > 0) sb.Append('\n');
                }
                sb.Append(line.TrimStart());
                blanks = 0;
            }
            var result = sb.ToString().Trim();
            return result.Length == 0 ? null : result;
        }

        /// <summary>
        /// Cleans a description that can arrive as plain text, as BBCode/markdown or as HTML,
        /// so every source hands Playnite the same plain text with normal line breaks.
        /// </summary>
        public static string CleanBlock(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var looksLikeHtml = Regex.IsMatch(value,
                @"<\s*/?\s*(?:br|p|div|span|li|ul|ol|a|b|i|u|em|strong|h[1-6])\b[^>]*>",
                RegexOptions.IgnoreCase);
            return looksLikeHtml ? StripHtml(value) : CleanDescription(StripMarkup(value));
        }

        /// <summary>
        /// Turns a plain text description into the HTML Playnite renders inside the description box.
        /// Playnite collapses raw newlines, so every line break has to be an explicit &lt;br&gt;.
        /// </summary>
        public static string ToHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var value = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var sb = new StringBuilder(value.Length + 32);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&':
                        sb.Append("&amp;");
                        break;
                    case '<':
                        sb.Append("&lt;");
                        break;
                    case '>':
                        sb.Append("&gt;");
                        break;
                    case '\n':
                        // Keep the newline after the tag: Playnite ignores it when rendering the HTML,
                        // but it keeps the stored description readable in the plain text editor.
                        sb.Append("<br>\n");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string JoinParagraphs(IEnumerable<string> parts)
        {
            if (parts == null) return null;
            var list = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            return list.Count == 0 ? null : CleanDescription(string.Join("\n\n", list));
        }

        public static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return null;
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            }
            return null;
        }

        /// <summary>Parses "2016-07-01", "2016/7/1", "2016-07", "2016" or ISO timestamps.</summary>
        public static void ParseDate(string value, out int? year, out int? month, out int? day)
        {
            year = null; month = null; day = null;
            if (string.IsNullOrWhiteSpace(value)) return;
            var s = value.Trim();
            int t = s.IndexOf('T');
            if (t > 0) s = s.Substring(0, t);
            s = s.Replace('/', '-').Replace('.', '-');
            var parts = s.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            int y, m, d;
            if (parts.Length >= 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) && y > 1900 && y < 2200) year = y;
            if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m) && m >= 1 && m <= 12) month = m;
            if (parts.Length >= 3)
            {
                var dayPart = parts[2];
                int keep = 0;
                while (keep < dayPart.Length && char.IsDigit(dayPart[keep])) keep++;
                if (keep > 0 && int.TryParse(dayPart.Substring(0, keep), NumberStyles.Integer, CultureInfo.InvariantCulture, out d) && d >= 1 && d <= 31) day = d;
            }
        }

        /// <summary>Maps a 0..max site score onto Playnite's 0..100 community score.</summary>
        public static int? ScoreToHundred(double? value, double max)
        {
            if (!value.HasValue || max <= 0) return null;
            if (value.Value <= 0) return null;
            var scaled = value.Value / max * 100.0;
            if (scaled > 100) scaled = 100;
            return (int)Math.Round(scaled);
        }
    }

    /// <summary>Downloads remote images so Playnite never has to guess headers.</summary>
    public static class Images
    {
        public static async Task<byte[]> TryDownload(string url, string referer, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            try
            {
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(referer)) headers["Referer"] = referer;
                return await Http.GetBytesAsync(url.Trim(), headers, 0, cancel).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }
    }
}

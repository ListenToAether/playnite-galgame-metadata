using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GalgameMetadata.Shared
{
    /// <summary>One search result as returned by a site, before the detail request.</summary>
    public class SearchHit
    {
        public string Id { get; set; }
        public string Title { get; set; }       // best display title
        public string AltTitle { get; set; }    // alternative (original / localized) title
        public string Developer { get; set; }
        public int? Year { get; set; }
        public string CoverUrl { get; set; }
        public string Url { get; set; }         // human facing page
        public JsonValue Raw { get; set; }      // site specific payload, kept for detail requests
    }

    /// <summary>User settings translated into scraper options.</summary>
    public class FetchOptions
    {
        public bool PreferChineseTitle { get; set; }
        public bool PreferChineseDescription { get; set; }
        public bool IncludeNsfw { get; set; }
        public int MaxTags { get; set; }
        public bool DownloadCover { get; set; }
        public bool DownloadScreenshots { get; set; }
        public bool AcceptBestGuess { get; set; }

        public FetchOptions()
        {
            MaxTags = 15;
            DownloadCover = true;
            DownloadScreenshots = true;
            IncludeNsfw = true;
        }
    }

    /// <summary>A data source implementation backed by one website's public API.</summary>
    public interface ISiteSource
    {
        string Name { get; }        // provider name shown by Playnite, e.g. "Ymgal"
        string SiteKey { get; }     // short stable key, used in log messages
        string HomeUrl { get; }     // used as Referer for image downloads

        bool TryParseId(string url, out string id);
        string PageUrl(string id);
        Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel);
        Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel);
    }

    /// <summary>Turns "this Playnite game" into "a scraped game from this site".</summary>
    public static class SourceResolver
    {
        public static async Task<ScrapedGame> ResolveAsync(
            ISiteSource source,
            string gameName,
            IEnumerable<string> existingUrls,
            FetchOptions options,
            CancellationToken cancel)
        {
            // 1. A link to the site already exists (previous scrape or manual association):
            //    that id is authoritative, no searching needed.
            if (existingUrls != null)
            {
                foreach (var url in existingUrls)
                {
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    string id;
                    if (!source.TryParseId(url, out id)) continue;
                    try
                    {
                        return await source.GetAsync(new SearchHit { Id = id, Title = gameName, Url = source.PageUrl(id) }, options, cancel)
                            .ConfigureAwait(false);
                    }
                    catch (ScrapeException)
                    {
                        // Stale link (entry deleted on the site) - fall back to name search.
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(gameName))
                throw new ScrapeException("This game has no name to search with.");

            var query = gameName.Trim();
            var hits = await source.SearchAsync(query, 15, options, cancel).ConfigureAwait(false);
            if (hits == null || hits.Count == 0)
                throw new ScrapeException(string.Format("{0} found no results for \"{1}\".", source.Name, query));

            var index = Text.BestIndex(hits, h => new[] { h.Title, h.AltTitle }, query, 0.62);
            if (index < 0)
            {
                if (options.AcceptBestGuess) index = 0;
                else
                {
                    // Search results often carry a localized title (for example 魔女的夜宴 for
                    // "サノバウィッチ"), so a weak title score does not mean the wrong entry.
                    // Confirm the first few candidates against their full title set before giving up.
                    int best = -1;
                    double bestScore = 0;
                    ScrapedGame confirmed = null;
                    var candidates = Math.Min(hits.Count, 3);
                    for (int i = 0; i < candidates; i++)
                    {
                        ScrapedGame candidate;
                        try
                        {
                            candidate = await source.GetAsync(hits[i], options, cancel).ConfigureAwait(false);
                        }
                        catch (ScrapeException)
                        {
                            continue;
                        }

                        var score = TitleScore(candidate, query);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = i;
                            confirmed = candidate;
                        }
                    }

                    if (confirmed != null && bestScore >= 0.8) return confirmed;
                    if (hits.Count == 1 && best == 0) return confirmed ?? await source.GetAsync(hits[0], options, cancel).ConfigureAwait(false);
                    throw new ScrapeException(string.Format(
                        "{0} found no confident match for \"{1}\". Closest result: \"{2}\".",
                        source.Name, query, hits[0].Title));
                }
            }

            return await source.GetAsync(hits[index], options, cancel).ConfigureAwait(false);
        }

        /// <summary>Best similarity of the query against every title a scraped entry knows about.</summary>
        private static double TitleScore(ScrapedGame game, string query)
        {
            double score = 0;
            var titles = new List<string> { game.Name, game.OriginalName, game.ChineseName };
            if (game.Aliases != null) titles.AddRange(game.Aliases);
            foreach (var title in titles)
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                score = Math.Max(score, Text.Similarity(title, query));
            }
            return score;
        }
    }

    /// <summary>Maps site specific platform codes onto Playnite platform names.</summary>
    public static class PlatformMap
    {
        public static string Map(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "win":
                case "windows":
                case "pc":
                case "win32":
                case "w":
                    return "PC (Windows)";
                case "and":
                case "android":
                case "andriod":
                case "apk":
                    return "Android";
                case "ios":
                case "iphone":
                case "ipad":
                    return "iOS";
                case "mac":
                case "macos":
                case "osx":
                    return "Mac";
                case "linux":
                    return "Linux";
                case "ps":
                case "ps1":
                case "psp":
                    return "Sony PlayStation";
                case "ps2":
                    return "Sony PlayStation 2";
                case "ps3":
                    return "Sony PlayStation 3";
                case "ps4":
                    return "Sony PlayStation 4";
                case "ps5":
                    return "Sony PlayStation 5";
                case "psv":
                case "psvita":
                    return "Sony PlayStation Vita";
                case "sw":
                case "switch":
                case "ns":
                    return "Nintendo Switch";
                case "web":
                case "browser":
                    return null; // not a Playnite platform
                default:
                    return null;
            }
        }

        public static void Add(List<string> target, string raw)
        {
            var mapped = Map(raw);
            if (mapped != null && !target.Contains(mapped)) target.Add(mapped);
        }
    }

    /// <summary>
    /// Drops tags that carry no library value: generic platform labels, release years and
    /// the developer / publisher names which Playnite already stores in its own fields.
    /// </summary>
    public static class TagFilter
    {
        private static readonly HashSet<string> Blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "galgame", "gal", "game", "游戏", "pc", "pc游戏", "steam", "adv", "avg", "r18", "18+", "成人",
            "汉化", "汉化版", "中文", "日文", "英文", "小游戏", "独立游戏", "同人"
        };

        private static readonly System.Text.RegularExpressions.Regex YearOnly =
            new System.Text.RegularExpressions.Regex(@"^\d{4}(\s*年(\s*\d{1,2}\s*月)?)?$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        public static bool IsJunk(string tag, ScrapedGame game, IEnumerable<string> brandNames)
        {
            if (string.IsNullOrWhiteSpace(tag)) return true;
            tag = tag.Trim();
            if (tag.Length < 2) return true;
            if (Blacklist.Contains(tag)) return true;
            if (YearOnly.IsMatch(tag)) return true;

            if (brandNames != null)
            {
                foreach (var brand in brandNames)
                {
                    if (string.Equals(brand, tag, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            if (game != null)
            {
                foreach (var name in game.Developers) if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var name in game.Publishers) if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var name in game.Aliases) if (string.Equals(name, tag, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(game.Name, tag, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(game.OriginalName, tag, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }

    /// <summary>Small URL helpers shared by the site sources.</summary>
    public static class UrlUtil
    {
        /// <summary>Returns the first capture group of <paramref name="pattern"/> or null.</summary>
        public static string Match(string url, string pattern)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(url, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var value = m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string Absolute(string baseUrl, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return path;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            return baseUrl + path.TrimStart('/');
        }

        public static string FileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "image";
            var clean = url;
            var q = clean.IndexOf('?');
            if (q >= 0) clean = clean.Substring(0, q);
            var slash = clean.LastIndexOf('/');
            var name = slash >= 0 ? clean.Substring(slash + 1) : clean;
            return string.IsNullOrEmpty(name) ? "image" : name;
        }
    }
}

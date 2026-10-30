using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Kungal
{
    /// <summary>
    /// Kungal (https://www.kungal.com) publishes its catalogue through the NextMoe catalog API.
    /// Anonymous access works; the API asks for a lenient rate limit and for nsfw=true to include R18 works.
    /// </summary>
    public class KungalSource : ISiteSource
    {
        private const string ApiBase = "https://api.nextmoe.dev/v2/catalog";
        private const int MinIntervalMs = 850;
        private const string DetailIncludes = "titles,refs,intros,companies,ratings,tags,playtimes,releases,screenshots";

        public string Name { get { return "Kungal"; } }
        public string SiteKey { get { return "Kungal"; } }
        public string HomeUrl { get { return "https://www.kungal.com/"; } }

        public bool TryParseId(string url, out string id)
        {
            id = UrlUtil.Match(url, @"kungal\.com/galgame/(\d+)")
                 ?? UrlUtil.Match(url, @"nextmoe\.dev/[^\s]*?/works/([A-Za-z0-9]+)");
            return id != null;
        }

        public string PageUrl(string id)
        {
            return "https://www.kungal.com/galgame/" + id;
        }

        private static Dictionary<string, string> Headers()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Accept", "application/json" },
                { "User-Agent", Http.DefaultUserAgent }
            };
        }

        public async Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            var url = ApiBase + "/works?q=" + Uri.EscapeDataString(query)
                      + "&nsfw=" + (options.IncludeNsfw ? "true" : "false")
                      + "&limit=" + Math.Max(1, limit)
                      + "&include=titles,companies"
                      + "&fields=medium,display_name,latin,localized,olang,content_rating,content_limit,release_date,cover,companies";

            var json = await Http.GetJsonAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);

            var hits = new List<SearchHit>();
            foreach (var item in J.Arr(json, "items"))
            {
                var medium = J.Str(item, "medium");
                if (!string.IsNullOrEmpty(medium) &&
                    !string.Equals(medium, "galgame", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(medium, "doujin_game", StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = J.Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;

                int? year, month, day;
                Text.ParseDate(J.Str(item, "release_date"), out year, out month, out day);

                var original = J.Str(item, "display_name");
                var chinese = J.Str(item, "localized.zh-Hans.value");
                var developer = FirstCompany(item, "developer") ?? FirstCompany(item, "publisher");

                hits.Add(new SearchHit
                {
                    Id = id,
                    Title = options.PreferChineseTitle
                        ? Text.FirstNonEmpty(chinese, original)
                        : Text.FirstNonEmpty(original, chinese),
                    AltTitle = options.PreferChineseTitle
                        ? Text.FirstNonEmpty(original, chinese)
                        : Text.FirstNonEmpty(chinese, original),
                    Developer = developer,
                    Year = year,
                    CoverUrl = J.Str(item, "cover.url"),
                    Url = PageUrl(id),
                    Raw = item
                });
            }
            return hits;
        }

        private static string FirstCompany(JsonValue work, string role)
        {
            foreach (var company in J.Arr(work, "companies"))
            {
                if (!string.Equals(J.Str(company, "attribution_role"), role, StringComparison.OrdinalIgnoreCase)) continue;
                var name = J.Str(company, "display_name");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return null;
        }

        /// <summary>Loads a work, following merged-entity redirects (the API answers 404 + current_id).</summary>
        private async Task<JsonValue> GetWorkAsync(string id, CancellationToken cancel)
        {
            var current = id;
            for (var hop = 0; hop < 4; hop++)
            {
                var url = ApiBase + "/works/" + Uri.EscapeDataString(current) +
                          "?include=" + DetailIncludes + "&nsfw=true&spoiler=none";
                var response = await Http.GetAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);

                if (response.IsSuccess)
                {
                    JsonValue json;
                    if (!JsonValue.TryParse(response.Body, out json) || json == null || !json.IsObject)
                        throw new ScrapeException("Kungal returned an unreadable work payload.");
                    return json;
                }

                if (response.StatusCode == 404)
                {
                    var moved = ParseMergedTarget(response.Body);
                    if (!string.IsNullOrEmpty(moved) && moved != current)
                    {
                        current = moved;
                        continue;
                    }
                    throw new ScrapeException("Kungal has no work with id " + current + ".");
                }

                throw new ScrapeException(string.Format("Kungal API returned HTTP {0}.", response.StatusCode));
            }
            throw new ScrapeException("Kungal kept redirecting to another work id.");
        }

        private static string ParseMergedTarget(string body)
        {
            JsonValue json;
            if (!JsonValue.TryParse(body, out json)) return null;
            var code = J.Str(json, "code");
            if (!string.Equals(code, "ENTITY_MERGED", StringComparison.OrdinalIgnoreCase)) return null;
            return J.Str(json, "current_id") ?? J.Str(json, "current_work_id");
        }

        public async Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) throw new ScrapeException("Kungal: no work id to load.");

            var work = await GetWorkAsync(hit.Id, cancel).ConfigureAwait(false);
            var id = J.Str(work, "id") ?? hit.Id;

            var original = J.Str(work, "display_name");
            var chinese = J.Str(work, "localized.zh-Hans.value");
            var english = J.Str(work, "localized.en.value");
            var latin = J.Str(work, "latin");

            var game = new ScrapedGame
            {
                SourceName = Name,
                SourceId = id,
                SiteUrl = PageUrl(id),
                OriginalName = original,
                ChineseName = chinese,
                Name = options.PreferChineseTitle
                    ? Text.FirstNonEmpty(chinese, original, english)
                    : Text.FirstNonEmpty(original, chinese, english),
                Description = PickIntro(work, options),
                CoverUrl = J.Str(work, "cover.url"),
                BannerUrl = J.Str(work, "banner.url")
            };

            int? year, month, day;
            Text.ParseDate(J.Str(work, "release_date"), out year, out month, out day);
            game.Year = year;
            game.Month = month;
            game.Day = day;

            var isAdult = string.Equals(J.Str(work, "content_rating"), "r18", StringComparison.OrdinalIgnoreCase);
            game.Nsfw = isAdult;
            game.AgeRating = isAdult ? "18+" : null;

            foreach (var title in J.Arr(work, "titles"))
            {
                var value = J.Str(title, "title");
                if (string.IsNullOrEmpty(value)) continue;
                if (Same(value, game.Name) || Same(value, original) || Same(value, chinese)) continue;
                if (!game.Aliases.Contains(value)) game.Aliases.Add(value);
            }
            foreach (var extra in new[] { english, latin })
            {
                if (string.IsNullOrEmpty(extra)) continue;
                if (Same(extra, game.Name) || Same(extra, original)) continue;
                if (!game.Aliases.Contains(extra)) game.Aliases.Add(extra);
            }

            AddCompanies(game, work, options);

            var tags = 0;
            foreach (var tag in J.Arr(work, "tags"))
            {
                if (options.MaxTags > 0 && tags >= options.MaxTags) break;
                if (string.Equals(J.Str(tag, "tier"), "hidden", StringComparison.OrdinalIgnoreCase)) continue;
                if (J.Bool(tag, "is_sexual") == true && !options.IncludeNsfw) continue;
                var name = J.Str(tag, "display_name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                name = name.Trim();
                if (game.Tags.Contains(name)) continue;
                game.Tags.Add(name);
                tags++;
            }

            ApplyScore(game, work);
            AddLinks(game, work, id);
            AddScreenshots(game, work, options);

            foreach (var release in J.Arr(work, "releases"))
            {
                foreach (var platform in J.Arr(release, "platforms"))
                    PlatformMap.Add(game.Platforms, platform.AsTrimmedString);
                PlatformMap.Add(game.Platforms, J.Str(release, "platform"));
            }

            return game;
        }

        /// <summary>
        /// NextMoe exposes the full screenshot set (include=screenshots, typically 20+ 1920x1080 images,
        /// mostly mirrored from VNDB) - that is the CG gallery the picker window shows on the right.
        /// </summary>
        private static void AddScreenshots(ScrapedGame game, JsonValue work, FetchOptions options)
        {
            foreach (var shot in J.Arr(work, "screenshots"))
            {
                var url = J.Str(shot, "url");
                if (string.IsNullOrWhiteSpace(url)) continue;
                var sexual = J.Str(shot, "sexual");
                if (!options.IncludeNsfw && !string.IsNullOrEmpty(sexual)
                    && !string.Equals(sexual, "safe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!game.Screenshots.Contains(url)) game.Screenshots.Add(url);
            }

            if (string.IsNullOrEmpty(game.BannerUrl) && game.Screenshots.Count > 0)
            {
                game.BannerUrl = game.Screenshots[0];
            }
        }

        private static string PickIntro(JsonValue work, FetchOptions options)
        {
            var languages = options.PreferChineseDescription
                ? new[] { "zh-Hans", "zh-Hant", "ja", "en" }
                : new[] { "ja", "zh-Hans", "zh-Hant", "en" };

            var intros = J.Arr(work, "intros").ToList();
            foreach (var language in languages)
            {
                foreach (var intro in intros)
                {
                    if (!string.Equals(J.Str(intro, "lang"), language, StringComparison.OrdinalIgnoreCase)) continue;
                    var text = Text.CleanBlock(J.Str(intro, "value"));
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            return null;
        }

        private static void AddCompanies(ScrapedGame game, JsonValue work, FetchOptions options)
        {
            foreach (var company in J.Arr(work, "companies"))
            {
                var role = J.Str(company, "attribution_role");
                var original = J.Str(company, "display_name");
                var chinese = J.Str(company, "localized.zh-Hans.value");
                var name = options.PreferChineseTitle
                    ? Text.FirstNonEmpty(chinese, original)
                    : Text.FirstNonEmpty(original, chinese);
                if (string.IsNullOrEmpty(name)) continue;

                if (string.Equals(role, "developer", StringComparison.OrdinalIgnoreCase))
                {
                    if (!game.Developers.Contains(name)) game.Developers.Add(name);
                }
                else if (string.Equals(role, "publisher", StringComparison.OrdinalIgnoreCase))
                {
                    if (!game.Publishers.Contains(name)) game.Publishers.Add(name);
                }
            }
        }

        /// <summary>Company names that NextMoe also publishes as tags (e.g. 柚子社, ゆずソフト).</summary>
        private static HashSet<string> CompanyNames(JsonValue work)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in J.Arr(work, "companies"))
            {
                foreach (var key in new[] { "display_name", "localized.zh-Hans.value", "localized.ja.value", "latin" })
                {
                    var value = J.Str(company, key);
                    if (!string.IsNullOrWhiteSpace(value)) names.Add(value.Trim());
                }
                foreach (var alias in J.StrList(company, "aliases"))
                {
                    if (!string.IsNullOrWhiteSpace(alias)) names.Add(alias.Trim());
                }
            }
            return names;
        }

        private static void ApplyScore(ScrapedGame game, JsonValue work)
        {
            var ratings = J.Arr(work, "ratings").ToList();
            var preference = new[] { "nextmoe", "vndb", "bangumi", "erogamescape", "howlongtobeat" };
            foreach (var source in preference)
            {
                foreach (var rating in ratings)
                {
                    if (!string.Equals(J.Str(rating, "source"), source, StringComparison.OrdinalIgnoreCase)) continue;
                    var score = J.Num(rating, "score");
                    if (!score.HasValue || score.Value <= 0) continue;
                    var max = string.Equals(source, "erogamescape", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(source, "howlongtobeat", StringComparison.OrdinalIgnoreCase)
                        ? 100.0
                        : 10.0;
                    var scaled = Text.ScoreToHundred(score.Value, max);
                    if (!scaled.HasValue) continue;
                    game.CommunityScore = scaled;
                    game.RawScore = score;
                    return;
                }
            }
        }

        private void AddLinks(ScrapedGame game, JsonValue work, string id)
        {
            game.AddLink("Kungal", PageUrl(id));

            foreach (var reference in J.Arr(work, "refs"))
            {
                var source = J.Str(reference, "source");
                var externalId = J.Str(reference, "external_id");
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(externalId)) continue;
                var url = ExternalUrl(source, externalId);
                if (string.IsNullOrEmpty(url)) continue;
                game.AddLink(Label(source), url);
                game.AssociatedSourceLinks.Add(new ScrapedLink(Label(source), url));
            }
        }

        private static string Label(string source)
        {
            switch (source.ToLowerInvariant())
            {
                case "vndb": return "VNDB";
                case "bangumi": return "Bangumi";
                case "steam": return "Steam";
                case "dlsite": return "DLsite";
                case "erogamescape": return "ErogameScape";
                case "getchu": return "Getchu";
                case "dmm": return "DMM";
                case "howlongtobeat": return "HowLongToBeat";
                default: return source;
            }
        }

        private static string ExternalUrl(string source, string externalId)
        {
            switch (source.ToLowerInvariant())
            {
                case "vndb":
                    return "https://vndb.org/" + externalId;
                case "bangumi":
                    return "https://bgm.tv/subject/" + externalId;
                case "steam":
                    return "https://store.steampowered.com/app/" + externalId;
                case "dlsite":
                    return "https://www.dlsite.com/maniax/work/=/product_id/" + externalId + ".html";
                case "erogamescape":
                    return "https://erogamescape.dyndns.org/~ap2/ero/toukei_kaiseki/game.php?game=" + externalId;
                case "getchu":
                    return "https://www.getchu.com/soft.phtml?id=" + externalId;
                default:
                    return null;
            }
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

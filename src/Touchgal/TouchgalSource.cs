using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Touchgal
{
    /// <summary>
    /// TouchGAL (https://www.touchgal.com) protects its website with a Cloudflare challenge, so the
    /// plugin talks to the official developer API instead: https://developer.touchgal.com/api/v1.
    /// That API requires a personal bearer token which the user creates on the developer site.
    /// Envelope: { success, data, error: { code, message } }.
    /// </summary>
    public class TouchgalSource : ISiteSource
    {
        private const string ApiBase = "https://developer.touchgal.com/api/v1";
        private const int MinIntervalMs = 600;

        public string Name { get { return "TouchGAL"; } }
        public string SiteKey { get { return "Touchgal"; } }
        public string HomeUrl { get { return "https://www.touchgal.com/"; } }

        /// <summary>Personal API token, filled from the plugin settings before every scrape.</summary>
        public string ApiToken { get; set; }

        public const string MissingTokenMessage =
            "TouchGAL needs a personal API token. Open https://developer.touchgal.com in a browser, " +
            "create a token, then paste it into this plugin's settings (Playnite > Settings > Extensions > TouchGAL).";

        public bool TryParseId(string url, out string id)
        {
            id = UrlUtil.Match(url, @"touchgal\.(?:com|ink|cc)/(?:galgame|game|games)/([A-Za-z0-9]{8})(?![A-Za-z0-9])");
            return id != null;
        }

        public string PageUrl(string id)
        {
            return "https://www.touchgal.com/galgame/" + id;
        }

        private Dictionary<string, string> Headers()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Accept", "application/json" },
                { "User-Agent", Http.DefaultUserAgent },
                { "Authorization", "Bearer " + (ApiToken ?? string.Empty).Trim() }
            };
        }

        private static JsonValue Unwrap(string body, string what)
        {
            JsonValue json;
            if (!JsonValue.TryParse(body, out json) || json == null || !json.IsObject)
                throw new ScrapeException("TouchGAL returned an unreadable " + what + " payload.");

            if (J.Bool(json, "success") != true)
            {
                var code = J.Str(json, "error.code");
                var message = J.Str(json, "error.message");
                if (string.Equals(code, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return null;
                if (string.Equals(code, "UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                    throw new ScrapeException("TouchGAL rejected the API token. " + MissingTokenMessage);
                throw new ScrapeException(string.Format("TouchGAL API error {0}: {1}", code, message));
            }
            return J.Get(json, "data");
        }

        public async Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(ApiToken))
                throw new ScrapeException(MissingTokenMessage);

            var url = ApiBase + "/games/search?keyword=" + Uri.EscapeDataString(query) +
                      "&page=1&limit=" + Math.Max(1, Math.Min(limit, 20)) +
                      "&allowNsfw=" + (options.IncludeNsfw ? "true" : "false");
            var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            var data = Unwrap(body, "search");

            var hits = new List<SearchHit>();
            if (data == null) return hits;

            foreach (var item in J.Arr(data, "items"))
            {
                var id = J.Str(item, "uniqueId");
                var title = J.Str(item, "name");
                if (string.IsNullOrEmpty(id)) continue;

                hits.Add(new SearchHit
                {
                    Id = id,
                    Title = title,
                    AltTitle = title,
                    Developer = J.FirstStr(item, "companies.0.name"),
                    CoverUrl = J.FirstStr(item, "coverUrl", "bannerUrl"),
                    Url = PageUrl(id),
                    Raw = item
                });
            }
            return hits;
        }

        private async Task<JsonValue> GetDetailAsync(string id, FetchOptions options, CancellationToken cancel)
        {
            var url = ApiBase + "/games/" + Uri.EscapeDataString(id) + "?allowNsfw=" +
                      (options.IncludeNsfw ? "true" : "false");
            var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            return Unwrap(body, "game");
        }

        public async Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) throw new ScrapeException("TouchGAL: no game id to load.");
            if (string.IsNullOrWhiteSpace(ApiToken)) throw new ScrapeException(MissingTokenMessage);

            var data = await GetDetailAsync(hit.Id, options, cancel).ConfigureAwait(false);
            if (data == null) throw new ScrapeException("TouchGAL has no game with id " + hit.Id + ".");

            var id = J.Str(data, "uniqueId") ?? hit.Id;
            var title = J.Str(data, "name");
            var page = Text.FirstNonEmpty(J.Str(data, "touchgalUrl"), PageUrl(id));

            var game = new ScrapedGame
            {
                SourceName = Name,
                SourceId = id,
                SiteUrl = page,
                Name = Text.FirstNonEmpty(title, hit.Title),
                OriginalName = J.FirstStr(data, "aliases.0"),
                ChineseName = title,
                Description = Text.CleanBlock(J.Str(data, "introduction")),
                CoverUrl = Text.FirstNonEmpty(J.Str(data, "coverUrl"), J.Str(data, "bannerUrl")),
                BannerUrl = J.Str(data, "bannerUrl")
            };

            int? year, month, day;
            Text.ParseDate(Text.FirstNonEmpty(J.Str(data, "releaseDate"), J.Str(data, "publishTime")), out year, out month, out day);
            game.Year = year;
            game.Month = month;
            game.Day = day;

            foreach (var alias in J.Arr(data, "aliases"))
            {
                var value = alias.AsTrimmedString;
                if (string.IsNullOrEmpty(value)) continue;
                if (Same(value, game.Name) || Same(value, game.ChineseName)) continue;
                if (!game.Aliases.Contains(value)) game.Aliases.Add(value);
            }

            foreach (var company in J.Arr(data, "companies"))
            {
                var name = J.Str(company, "name");
                if (string.IsNullOrEmpty(name)) continue;
                if (!game.Developers.Contains(name)) game.Developers.Add(name);
                foreach (var alias in J.Arr(company, "aliases"))
                {
                    var value = alias.AsTrimmedString;
                    if (!string.IsNullOrEmpty(value) && !game.Aliases.Contains(value) && !Same(value, name))
                        game.Aliases.Add(value);
                }
            }

            var tagCount = 0;
            foreach (var tag in J.Arr(data, "tags"))
            {
                if (options.MaxTags > 0 && tagCount >= options.MaxTags) break;
                var value = tag.AsTrimmedString;
                if (string.IsNullOrEmpty(value)) continue;
                if (game.Tags.Contains(value)) continue;
                game.Tags.Add(value);
                tagCount++;
            }

            foreach (var type in J.Arr(data, "type"))
            {
                var value = type.AsTrimmedString;
                if (string.IsNullOrEmpty(value)) continue;
                if (string.Equals(value, "galgame", StringComparison.OrdinalIgnoreCase)) continue;
                if (!game.Genres.Contains(value)) game.Genres.Add(value);
            }

            foreach (var platform in J.Arr(data, "platform"))
                PlatformMap.Add(game.Platforms, platform.AsTrimmedString);

            var score = J.Num(data, "rating.average");
            var votes = J.Int(data, "rating.count");
            if (score.HasValue && score.Value > 0 && (votes ?? 0) > 0)
            {
                var normalized = score.Value <= 10 ? score.Value * 10d : score.Value;
                game.CommunityScore = (int)Math.Round(Math.Max(0, Math.Min(100, normalized)));
                game.RawScore = score.Value;
            }

            game.Links.Add(new ScrapedLink("TouchGAL", page));
            return game;
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

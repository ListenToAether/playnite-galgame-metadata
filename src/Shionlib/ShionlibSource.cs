using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Shionlib
{
    /// <summary>
    /// Shionlib (https://shionlib.com, 书音的图书馆) exposes a public JSON API without authentication.
    /// Envelope: { code, message, data, requestId, timestamp } with code 0 meaning success.
    /// </summary>
    public class ShionlibSource : ISiteSource
    {
        private const string ApiBase = "https://shionlib.com/api";
        private const int MinIntervalMs = 250;

        public string Name { get { return "Shionlib"; } }
        public string SiteKey { get { return "Shionlib"; } }
        public string HomeUrl { get { return "https://shionlib.com/"; } }

        public bool TryParseId(string url, out string id)
        {
            id = UrlUtil.Match(url, @"shionlib\.com/(?:[a-z]{2}/)?game/(\d+)");
            return id != null;
        }

        public string PageUrl(string id)
        {
            return "https://shionlib.com/zh/game/" + id;
        }

        private static Dictionary<string, string> Headers()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Accept", "application/json" },
                { "User-Agent", Http.DefaultUserAgent }
            };
        }

        private static JsonValue Unwrap(string body, string what)
        {
            JsonValue json;
            if (!JsonValue.TryParse(body, out json) || json == null || !json.IsObject)
                throw new ScrapeException("Shionlib returned an unreadable " + what + " payload.");

            var code = J.Int(json, "code");
            var message = J.Str(json, "message");

            if (code.HasValue && code.Value != 0)
            {
                if (code.Value == 404 || code.Value == 400101) return null;
                throw new ScrapeException(string.Format("Shionlib API error {0}: {1}", code.Value, message));
            }
            return J.Get(json, "data");
        }

        public async Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            var url = ApiBase + "/search?q=" + Uri.EscapeDataString(query) +
                      "&page=1&pageSize=" + Math.Max(1, Math.Min(limit, 50));
            var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            var data = Unwrap(body, "search");

            var hits = new List<SearchHit>();
            if (data == null) return hits;

            foreach (var item in J.Arr(data, "games.items"))
            {
                var id = J.Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;

                int? year, month, day;
                Text.ParseDate(J.Str(item, "release_date"), out year, out month, out day);

                var japanese = J.Str(item, "title_jp");
                var chinese = J.Str(item, "title_zh");
                var english = J.Str(item, "title_en");
                var cover = FirstCover(item);

                hits.Add(new SearchHit
                {
                    Id = id,
                    Title = TitleFor(options, chinese, japanese, english),
                    AltTitle = AltTitleFor(options, chinese, japanese, english),
                    Developer = FirstDeveloper(item),
                    Year = year,
                    CoverUrl = cover,
                    Url = PageUrl(id),
                    Raw = item
                });
            }
            return hits;
        }

        private static string FirstCover(JsonValue item)
        {
            foreach (var cover in J.Arr(item, "covers"))
            {
                var url = J.Str(cover, "url");
                if (!string.IsNullOrEmpty(url)) return url;
            }
            return null;
        }

        private static string FirstDeveloper(JsonValue item)
        {
            foreach (var entry in J.Arr(item, "developers"))
            {
                var name = J.Str(entry, "developer.name");
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return null;
        }

        private static string TitleFor(FetchOptions options, string chinese, string japanese, string english)
        {
            return options.PreferChineseTitle
                ? Text.FirstNonEmpty(chinese, japanese, english)
                : Text.FirstNonEmpty(japanese, chinese, english);
        }

        private static string AltTitleFor(FetchOptions options, string chinese, string japanese, string english)
        {
            return options.PreferChineseTitle
                ? Text.FirstNonEmpty(japanese, english, chinese)
                : Text.FirstNonEmpty(chinese, english, japanese);
        }

        public async Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) throw new ScrapeException("Shionlib: no game id to load.");

            var headerUrl = ApiBase + "/game/" + Uri.EscapeDataString(hit.Id) + "/header";
            var headerBody = await Http.GetStringAsync(headerUrl, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            var header = Unwrap(headerBody, "game");
            if (header == null) throw new ScrapeException("Shionlib has no game with id " + hit.Id + ".");

            var detailsUrl = ApiBase + "/game/" + Uri.EscapeDataString(hit.Id) + "/details";
            var detailsBody = await Http.GetStringAsync(detailsUrl, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            var details = Unwrap(detailsBody, "details") ?? JsonValue.Null;

            var japanese = J.Str(header, "title_jp");
            var chinese = J.Str(header, "title_zh");
            var english = J.Str(header, "title_en");
            var id = J.Str(header, "id") ?? hit.Id;

            var game = new ScrapedGame
            {
                SourceName = Name,
                SourceId = id,
                SiteUrl = PageUrl(id),
                OriginalName = japanese,
                ChineseName = chinese,
                Name = TitleFor(options, chinese, japanese, english),
                Description = PickIntro(details, options),
                CoverUrl = BestCover(header, options),
                BannerUrl = J.Str(J.Arr(details, "images").FirstOrDefault(), "url")
            };

            int? year, month, day;
            Text.ParseDate(J.Str(header, "release_date"), out year, out month, out day);
            game.Year = year;
            game.Month = month;
            game.Day = day;

            foreach (var alias in J.Arr(header, "aliases"))
            {
                var value = alias.AsTrimmedString;
                if (string.IsNullOrEmpty(value)) continue;
                if (Same(value, game.Name) || Same(value, japanese) || Same(value, chinese) || Same(value, english)) continue;
                if (!game.Aliases.Contains(value)) game.Aliases.Add(value);
            }

            foreach (var entry in J.Arr(header, "developers"))
            {
                var name = J.Str(entry, "developer.name");
                if (string.IsNullOrEmpty(name)) continue;
                if (!game.Developers.Contains(name)) game.Developers.Add(name);
            }


            var tags = 0;
            foreach (var entry in J.Arr(details, "tags"))
            {
                if (options.MaxTags > 0 && tags >= options.MaxTags) break;
                var name = J.Str(entry, "tag.name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                name = name.Trim();
                if (game.Tags.Contains(name)) continue;
                game.Tags.Add(name);
                tags++;
            }

            foreach (var image in J.Arr(details, "images"))
            {
                var url = J.Str(image, "url");
                if (!string.IsNullOrEmpty(url) && !game.Screenshots.Contains(url)) game.Screenshots.Add(url);
            }

            var isAdult = J.Bool(details, "nsfw") == true;
            game.Nsfw = isAdult;
            if (isAdult) game.AgeRating = "18+";

            AddLinks(game, header, details);

            foreach (var platform in J.Arr(header, "platform"))
                PlatformMap.Add(game.Platforms, platform.AsTrimmedString);

            var type = J.Str(header, "type");
            if (!string.IsNullOrEmpty(type) && !Same(type, "ADV")) game.Genres.Add(type);

            return game;
        }

        private static string PickIntro(JsonValue details, FetchOptions options)
        {
            var order = options.PreferChineseDescription
                ? new[] { "intro_zh", "intro_jp", "intro_en" }
                : new[] { "intro_jp", "intro_zh", "intro_en" };

            foreach (var key in order)
            {
                var text = Text.CleanBlock(J.Str(details, key));
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static string BestCover(JsonValue header, FetchOptions options)
        {
            string masked = null;
            foreach (var cover in J.Arr(header, "covers"))
            {
                var url = J.Str(cover, "url");
                if (string.IsNullOrEmpty(url)) continue;
                if (J.Bool(cover, "masked") == true)
                {
                    if (masked == null) masked = url;
                    continue;
                }
                return url;
            }
            // R18 covers come back blurred for guests; the query string holds the blur parameters.
            if (masked != null && options.IncludeNsfw)
            {
                var index = masked.IndexOf('?');
                return index > 0 ? masked.Substring(0, index) : masked;
            }
            return masked;
        }

        private void AddLinks(ScrapedGame game, JsonValue header, JsonValue details)
        {
            game.AddLink("Shionlib", game.SiteUrl);

            var vndb = J.Str(header, "v_id");
            if (!string.IsNullOrEmpty(vndb))
                game.AddLink("VNDB", "https://vndb.org/" + vndb.TrimStart('v', 'V'));

            var bangumi = J.Str(header, "b_id");
            if (!string.IsNullOrEmpty(bangumi))
                game.AddLink("Bangumi", "https://bgm.tv/subject/" + bangumi);

            var hikarinagi = J.Str(header, "h_id");
            if (!string.IsNullOrEmpty(hikarinagi) && hikarinagi != "0")
                game.AddLink("Hikarinagi", "https://www.hikarinagi.org/galgames/" + hikarinagi);

            foreach (var link in J.Arr(details, "link"))
            {
                var url = J.Str(link, "url");
                if (string.IsNullOrEmpty(url)) continue;
                var label = Text.FirstNonEmpty(J.Str(link, "label"), J.Str(link, "name"), "Website");
                game.AddLink(label, url);
            }
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

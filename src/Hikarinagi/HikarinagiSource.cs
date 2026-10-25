using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Hikarinagi
{
    /// <summary>
    /// Hikarinagi (https://www.hikarinagi.org) serves its public JSON API from the website itself
    /// under /api/v3 (the separate api.hikarinagi.org host is token gated).
    /// Envelope: { success, data, error, request_id, timestamp }.
    /// </summary>
    public class HikarinagiSource : ISiteSource
    {
        private const string ApiBase = "https://www.hikarinagi.org/api/v3";
        private const string ImageBase = "https://images.yurari.moe/";
        private const int MinIntervalMs = 300;

        private static bool HasAdultMarker(JsonValue detail, List<JsonValue> tags)
        {
            foreach (var cover in J.Arr(detail, "covers"))
                if (J.Bool(cover, "media.nsfw") == true) return true;

            foreach (var image in J.Arr(detail, "images"))
                if (J.Bool(image, "nsfw") == true) return true;

            foreach (var entry in tags)
            {
                var name = J.Str(entry, "tag.name");
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("R18", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (name.IndexOf("18禁", StringComparison.Ordinal) >= 0) return true;
                if (string.Equals(name, "成人", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string Name { get { return "Hikarinagi"; } }
        public string SiteKey { get { return "Hikarinagi"; } }
        public string HomeUrl { get { return "https://www.hikarinagi.org/"; } }

        public bool TryParseId(string url, out string id)
        {
            id = UrlUtil.Match(url, @"hikarinagi\.org/galgames/(\d+)");
            return id != null;
        }

        public string PageUrl(string id)
        {
            return "https://www.hikarinagi.org/galgames/" + id;
        }

        private static Dictionary<string, string> Headers()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Accept", "application/json" },
                { "User-Agent", Http.DefaultUserAgent }
            };
        }

        private static string ImageUrl(string relative)
        {
            if (string.IsNullOrEmpty(relative)) return null;
            if (relative.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return relative;
            return ImageBase + relative.TrimStart('/');
        }

        private static JsonValue Unwrap(string body, string what)
        {
            JsonValue json;
            if (!JsonValue.TryParse(body, out json) || json == null || !json.IsObject)
                throw new ScrapeException("Hikarinagi returned an unreadable " + what + " payload.");

            if (J.Bool(json, "success") == false)
            {
                var code = J.Str(json, "error.code");
                var message = J.Str(json, "error.message");
                if (string.Equals(code, "COMMON_NOT_FOUND", StringComparison.OrdinalIgnoreCase)) return null;
                throw new ScrapeException(string.Format("Hikarinagi API error {0}: {1}", code, message));
            }
            return J.Get(json, "data");
        }

        public async Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            var url = ApiBase + "/search?q=" + Uri.EscapeDataString(query) +
                      "&types=galgame&page=1&page_size=" + Math.Max(1, Math.Min(limit, 30));
            var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            var data = Unwrap(body, "search");

            var hits = new List<SearchHit>();
            if (data == null) return hits;

            foreach (var item in J.Arr(data, "items"))
            {
                var type = J.Str(item, "type");
                if (!string.IsNullOrEmpty(type) && !string.Equals(type, "galgame", StringComparison.OrdinalIgnoreCase)) continue;

                var id = J.Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;

                int? year, month, day;
                Text.ParseDate(J.Str(item, "subtitle"), out year, out month, out day);

                var title = J.Str(item, "title");
                hits.Add(new SearchHit
                {
                    Id = id,
                    Title = title,
                    AltTitle = title,
                    Developer = J.Str(item, "developer"),
                    Year = year,
                    CoverUrl = ImageUrl(J.Str(item, "cover.src")),
                    Url = PageUrl(id),
                    Raw = item
                });
            }
            return hits;
        }

        private async Task<JsonValue> GetDetailAsync(string id, CancellationToken cancel)
        {
            var url = ApiBase + "/galgames/" + Uri.EscapeDataString(id);
            var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
            return Unwrap(body, "game");
        }

        private async Task<List<JsonValue>> GetCollectionAsync(string id, string child, CancellationToken cancel)
        {
            var url = ApiBase + "/galgames/" + Uri.EscapeDataString(id) + "/" + child;
            try
            {
                var body = await Http.GetStringAsync(url, Headers(), MinIntervalMs, cancel).ConfigureAwait(false);
                var data = Unwrap(body, child);
                return data == null ? new List<JsonValue>() : J.Arr(data, "").ToList();
            }
            catch (ScrapeException)
            {
                return new List<JsonValue>();
            }
        }

        public async Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) throw new ScrapeException("Hikarinagi: no game id to load.");

            var detail = await GetDetailAsync(hit.Id, cancel).ConfigureAwait(false);
            if (detail == null) throw new ScrapeException("Hikarinagi has no game with id " + hit.Id + ".");

            var producersTask = GetCollectionAsync(hit.Id, "producers", cancel);
            var tagsTask = GetCollectionAsync(hit.Id, "tags", cancel);
            var staffTask = GetCollectionAsync(hit.Id, "staff", cancel);
            await Task.WhenAll(producersTask, tagsTask, staffTask).ConfigureAwait(false);

            var id = J.Str(detail, "id") ?? hit.Id;
            var original = J.Str(detail, "origin_title");
            var translated = J.Str(detail, "trans_title");
            var english = J.Str(detail, "en_title");

            var game = new ScrapedGame
            {
                SourceName = Name,
                SourceId = id,
                SiteUrl = PageUrl(id),
                OriginalName = original,
                ChineseName = translated,
                Name = options.PreferChineseTitle
                    ? Text.FirstNonEmpty(translated, original, english, hit.Title)
                    : Text.FirstNonEmpty(original, translated, english, hit.Title),
                Description = PickIntro(detail, options),
                CoverUrl = BestCover(detail, options),
                BannerUrl = J.Str(J.Arr(detail, "images").FirstOrDefault(), "src")
            };

            if (!string.IsNullOrEmpty(game.BannerUrl)) game.BannerUrl = ImageUrl(game.BannerUrl);

            int? year, month, day;
            Text.ParseDate(J.Str(detail, "release_date"), out year, out month, out day);
            game.Year = year;
            game.Month = month;
            game.Day = day;

            foreach (var alias in J.Arr(detail, "aliases"))
            {
                var value = alias.AsTrimmedString;
                if (string.IsNullOrEmpty(value)) continue;
                if (Same(value, game.Name) || Same(value, original) || Same(value, translated)) continue;
                if (!game.Aliases.Contains(value)) game.Aliases.Add(value);
            }

            AddProducers(game, producersTask.Result);

            var tags = 0;
            var developerNames = new HashSet<string>(game.Developers, StringComparer.OrdinalIgnoreCase);
            // Staff entries are artists, writers and voice actors; their names show up as tags
            // but are noise in a Playnite tag list, so drop them the same way as brand names.
            foreach (var entry in staffTask.Result)
            {
                var person = J.Get(entry, "person");
                foreach (var field in new[] { "name", "trans_name" })
                {
                    var personName = J.Str(person, field);
                    if (!string.IsNullOrEmpty(personName)) developerNames.Add(personName);
                }
            }

            foreach (var entry in tagsTask.Result)
            {
                if (options.MaxTags > 0 && tags >= options.MaxTags) break;
                var name = J.Str(entry, "tag.name");
                if (TagFilter.IsJunk(name, game, developerNames)) continue;
                if (game.Tags.Contains(name)) continue;
                game.Tags.Add(name);
                tags++;
            }

            foreach (var image in J.Arr(detail, "images"))
            {
                var url = ImageUrl(J.Str(image, "src"));
                if (string.IsNullOrEmpty(url)) continue;
                if (J.Bool(image, "nsfw") == true && !options.IncludeNsfw) continue;
                if (!game.Screenshots.Contains(url)) game.Screenshots.Add(url);
            }

            // The detail "nsfw" flag is often false even for R18 works, so also look at the
            // cover assets and the tags before deciding whether to mark the game as adult.
            var isAdult = J.Bool(detail, "nsfw") == true || HasAdultMarker(detail, tagsTask.Result);
            game.Nsfw = isAdult;
            if (isAdult) game.AgeRating = "18+";

            var advType = J.Str(detail, "adv_type");
            if (!string.IsNullOrEmpty(advType) && !string.Equals(advType, "ADV", StringComparison.OrdinalIgnoreCase))
                game.Genres.Add(advType);

            var engine = J.Str(detail, "engine");
            if (!string.IsNullOrEmpty(engine)) game.Features.Add(engine);

            AddLinks(game, detail);
            foreach (var platform in J.Arr(detail, "platforms"))
                PlatformMap.Add(game.Platforms, platform.AsTrimmedString);

            return game;
        }

        private static string PickIntro(JsonValue detail, FetchOptions options)
        {
            var order = options.PreferChineseDescription
                ? new[] { "trans_intro", "origin_intro", "en_intro" }
                : new[] { "origin_intro", "trans_intro", "en_intro" };

            foreach (var key in order)
            {
                var text = Text.CleanDescription(Text.StripMarkup(J.Str(detail, key)));
                if (!string.IsNullOrEmpty(text)) return text;
            }
            return null;
        }

        private static string BestCover(JsonValue detail, FetchOptions options)
        {
            string fallback = null;
            foreach (var cover in J.Arr(detail, "covers"))
            {
                var url = ImageUrl(J.Str(cover, "media.src"));
                if (string.IsNullOrEmpty(url)) continue;
                if (J.Bool(cover, "media.nsfw") == true)
                {
                    if (fallback == null) fallback = url;
                    continue;
                }
                return url;
            }
            return fallback;
        }

        private static void AddProducers(ScrapedGame game, List<JsonValue> producers)
        {
            foreach (var entry in producers)
            {
                var role = J.Str(entry, "role");
                var name = J.Str(entry, "producer.name");
                if (string.IsNullOrEmpty(name)) continue;

                if (string.Equals(role, "DEVELOPER", StringComparison.OrdinalIgnoreCase))
                {
                    if (!game.Developers.Contains(name)) game.Developers.Add(name);
                }
                else if (string.Equals(role, "PUBLISHER", StringComparison.OrdinalIgnoreCase))
                {
                    if (!game.Publishers.Contains(name)) game.Publishers.Add(name);
                }
            }
        }

        private void AddLinks(ScrapedGame game, JsonValue detail)
        {
            const int MaxLinks = 12;
            game.AddLink("Hikarinagi", game.SiteUrl, MaxLinks);

            var vndb = J.Str(detail, "vndb_id");
            if (!string.IsNullOrEmpty(vndb) && vndb != "0")
                game.AddLink("VNDB", "https://vndb.org/" + vndb.TrimStart('v', 'V'), MaxLinks);

            var bangumi = J.Str(detail, "bangumi_game_id");
            if (!string.IsNullOrEmpty(bangumi) && bangumi != "0")
                game.AddLink("Bangumi", "https://bgm.tv/subject/" + bangumi, MaxLinks);

            foreach (var app in J.Arr(detail, "steam_apps"))
            {
                var appId = J.Str(app, "app_id");
                if (string.IsNullOrEmpty(appId)) continue;
                game.AddLink("Steam", "https://store.steampowered.com/app/" + appId, MaxLinks);
            }

            var homepage = J.Str(detail, "homepage");
            if (!string.IsNullOrEmpty(homepage))
                game.AddLink("Official website", homepage, MaxLinks);

            foreach (var link in J.Arr(detail, "external_links"))
            {
                var url = J.Str(link, "url");
                if (string.IsNullOrEmpty(url)) continue;
                var label = Text.FirstNonEmpty(J.Str(link, "label"), J.Str(link, "name"), "Website");
                game.AddLink(label, url, MaxLinks);
            }
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

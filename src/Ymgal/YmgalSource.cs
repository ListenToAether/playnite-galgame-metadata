using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GalgameMetadata.Shared;

namespace GalgameMetadata.Ymgal
{
    /// <summary>
    /// Ymgal (月幕Galgame) public archive API.
    /// Auth: OAuth client credentials, then a bearer token valid for about an hour.
    /// Docs used by the community clients (ReinaManager): /open/archive/search-game and /open/archive.
    /// </summary>
    public class YmgalSource : ISiteSource
    {
        private const string BaseUrl = "https://www.ymgal.games";
        private const string TokenUrl =
            BaseUrl + "/oauth/token?grant_type=client_credentials&client_id=ymgal&client_secret=luna0327&scope=public";
        private const int MinIntervalMs = 550;

        private static readonly object TokenSync = new object();
        private static string _token;
        private static DateTime _tokenExpiry = DateTime.MinValue;

        public string Name { get { return "Ymgal"; } }
        public string SiteKey { get { return "Ymgal"; } }
        public string HomeUrl { get { return BaseUrl + "/"; } }

        public bool TryParseId(string url, out string id)
        {
            id = UrlUtil.Match(url, @"ymgal\.games/ga/?(\d+)") ?? UrlUtil.Match(url, @"[?&]gid=(\d+)");
            return id != null;
        }

        public string PageUrl(string id)
        {
            return BaseUrl + "/ga" + id;
        }

        private async Task<string> GetTokenAsync(CancellationToken cancel)
        {
            lock (TokenSync)
            {
                if (!string.IsNullOrEmpty(_token) && DateTime.UtcNow < _tokenExpiry) return _token;
            }

            var json = await Http.GetJsonAsync(TokenUrl, null, MinIntervalMs, cancel).ConfigureAwait(false);
            var token = J.Str(json, "access_token");
            if (string.IsNullOrEmpty(token))
                throw new ScrapeException("Ymgal did not return an access token.");

            var lifetime = J.Int(json, "expires_in") ?? 3600;
            lock (TokenSync)
            {
                _token = token;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(60, lifetime - 120));
            }
            return token;
        }

        private static void InvalidateToken()
        {
            lock (TokenSync)
            {
                _token = null;
                _tokenExpiry = DateTime.MinValue;
            }
        }

        private async Task<JsonValue> ApiGetAsync(string url, CancellationToken cancel)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var token = await GetTokenAsync(cancel).ConfigureAwait(false);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Authorization", "Bearer " + token },
                    { "version", "1" },
                    { "Accept", "application/json;charset=utf-8" }
                };

                var json = await Http.GetJsonAsync(url, headers, MinIntervalMs, cancel).ConfigureAwait(false);
                var code = J.Int(json, "code");
                if (code.HasValue && (code.Value == 401 || code.Value == 403 || code.Value == 429))
                {
                    InvalidateToken();
                    continue;
                }
                if (code.HasValue && code.Value != 0)
                {
                    var message = J.Str(json, "msg");
                    throw new ScrapeException("Ymgal error: " + (string.IsNullOrEmpty(message) ? code.Value.ToString() : message));
                }
                return json;
            }
            throw new ScrapeException("Ymgal rejected the access token repeatedly.");
        }

        public async Task<List<SearchHit>> SearchAsync(string query, int limit, FetchOptions options, CancellationToken cancel)
        {
            var url = BaseUrl + "/open/archive/search-game?mode=list&keyword=" + Uri.EscapeDataString(query) +
                      "&pageNum=1&pageSize=" + Math.Max(1, limit);
            var json = await ApiGetAsync(url, cancel).ConfigureAwait(false);

            var hits = new List<SearchHit>();
            foreach (var item in J.Arr(json, "data.result"))
            {
                var id = J.Str(item, "id");
                if (string.IsNullOrEmpty(id)) continue;

                int? year, month, day;
                Text.ParseDate(J.Str(item, "releaseDate"), out year, out month, out day);
                var chinese = J.Str(item, "chineseName");
                var original = J.Str(item, "name");
                hits.Add(new SearchHit
                {
                    Id = id,
                    Title = options.PreferChineseTitle ? Text.FirstNonEmpty(chinese, original) : Text.FirstNonEmpty(original, chinese),
                    AltTitle = options.PreferChineseTitle ? Text.FirstNonEmpty(original, chinese) : Text.FirstNonEmpty(chinese, original),
                    Developer = J.Str(item, "orgName"),
                    Year = year,
                    CoverUrl = J.Str(item, "mainImg"),
                    Url = PageUrl(id)
                });
            }
            return hits;
        }

        public async Task<ScrapedGame> GetAsync(SearchHit hit, FetchOptions options, CancellationToken cancel)
        {
            if (hit == null || string.IsNullOrEmpty(hit.Id)) throw new ScrapeException("Ymgal: no game id to load.");

            var json = await ApiGetAsync(BaseUrl + "/open/archive?gid=" + Uri.EscapeDataString(hit.Id), cancel)
                .ConfigureAwait(false);
            var node = J.Get(json, "data.game");
            if (node.IsNull) throw new ScrapeException("Ymgal returned no game for id " + hit.Id);

            var gid = J.Str(node, "gid") ?? hit.Id;
            var original = J.Str(node, "name");
            var chinese = J.Str(node, "chineseName");

            var game = new ScrapedGame
            {
                SourceName = Name,
                SourceId = gid,
                SiteUrl = PageUrl(gid),
                OriginalName = original,
                ChineseName = chinese,
                Name = options.PreferChineseTitle
                    ? Text.FirstNonEmpty(chinese, original)
                    : Text.FirstNonEmpty(original, chinese),
                Description = Text.CleanDescription(J.Str(node, "introduction")),
                CoverUrl = J.Str(node, "mainImg"),
                Nsfw = J.Bool(node, "restricted") ?? false
            };
            game.AgeRating = game.Nsfw ? "18+" : null;

            int? month, day, year;
            Text.ParseDate(J.Str(node, "releaseDate"), out year, out month, out day);
            game.Year = year;
            game.Month = month;
            game.Day = day;

            var typeDesc = J.Str(node, "typeDesc");
            if (!string.IsNullOrEmpty(typeDesc)) game.Genres.Add(typeDesc);

            foreach (var alias in J.Arr(node, "extensionName"))
            {
                var value = J.Str(alias, "name");
                if (string.IsNullOrEmpty(value)) continue;
                if (Same(value, game.Name) || Same(value, game.OriginalName) || Same(value, game.ChineseName)) continue;
                if (!game.Aliases.Contains(value)) game.Aliases.Add(value);
            }

            await AddDeveloperAsync(game, J.Str(node, "developerId"), options, cancel).ConfigureAwait(false);
            AddPublishers(game, J.Arr(node, "moreEntry"));

            game.Links.Add(new ScrapedLink("Ymgal", game.SiteUrl));
            foreach (var site in J.Arr(node, "website"))
            {
                var url = J.Str(site, "link");
                if (string.IsNullOrEmpty(url)) continue;
                var title = J.Str(site, "title");
                game.Links.Add(new ScrapedLink(string.IsNullOrEmpty(title) ? "Website" : title, url));
            }

            foreach (var release in J.Arr(node, "releases"))
                PlatformMap.Add(game.Platforms, J.Str(release, "platform"));

            return game;
        }

        private async Task AddDeveloperAsync(ScrapedGame game, string developerId, FetchOptions options, CancellationToken cancel)
        {
            if (string.IsNullOrEmpty(developerId)) return;
            try
            {
                var json = await ApiGetAsync(BaseUrl + "/open/archive?orgId=" + Uri.EscapeDataString(developerId), cancel)
                    .ConfigureAwait(false);
                var org = J.Get(json, "data.org");
                var name = J.Str(org, "name");
                var chineseName = J.Str(org, "chineseName");
                var display = options.PreferChineseTitle
                    ? Text.FirstNonEmpty(chineseName, name)
                    : Text.FirstNonEmpty(name, chineseName);
                if (!string.IsNullOrEmpty(display)) game.Developers.Add(display);
            }
            catch (Exception)
            {
                // Developer lookup is nice to have only.
            }
        }

        private static void AddPublishers(ScrapedGame game, IEnumerable<JsonValue> moreEntries)
        {
            foreach (var entry in moreEntries)
            {
                if (!string.Equals(J.Str(entry, "key"), "发行商", StringComparison.Ordinal)) continue;
                var value = J.Str(entry, "value");
                if (string.IsNullOrEmpty(value)) continue;
                foreach (var part in value.Split(new[] { '、', ',', '，', '/', '／' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var publisher = part.Trim();
                    if (publisher.Length > 0 && !game.Publishers.Contains(publisher)) game.Publishers.Add(publisher);
                }
            }
        }

        private static bool Same(string a, string b)
        {
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
                   string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

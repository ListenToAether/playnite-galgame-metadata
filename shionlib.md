# Shionlib (书音的图书馆) public HTTP API — reverse-engineered spec

Target: a C# .NET Framework 4.6.2 Playnite metadata plugin that searches games by name and
fetches full game details by id.

* Site: `https://shionlib.com` (Chinese galgame / visual-novel database)
* API base URL: **`https://shionlib.com`**, path prefix **`/api`** (no version segment)
* Status of this document: every request/response below was executed on 2026-10-25 from a Windows
  machine with `curl.exe`, plain HTTP/1.1 over TLS 1.2, no cookie, no API key, no browser
  fingerprint. Status codes and byte counts are real. Guesses are explicitly marked **[GUESS]**.
* Provenance: the site is a React Router 7 SSR SPA whose data layer proxies `/api/*` to a Go
  backend. The Go source is public (`github.com/Ringyuki/shionlib`, 32 MB zip, downloadable via
  `https://codeload.github.com/Ringyuki/shionlib/zip/refs/heads/main`) and it ships a full
  OpenAPI 3.1 document at `apps/api/openapi/openapi.json` inside that zip. The endpoints and field
  semantics below were cross-checked against that document **and** against live responses, so they
  do not depend on the repo staying public.

---

## 1. Conventions

### 1.1 Envelope

Every `/api/...` JSON response is wrapped:

```json
{"code":0,"message":"","data":{ },"requestId":"0f2b0b92-0d89-43a3-bb02-3439c7037ea8","timestamp":"2026-10-25T10:32:05.741Z"}
```

| field | type | meaning |
|---|---|---|
| `code` | int | `0` = success. Otherwise a business code (see 6) or the HTTP status when no business code applies. Always trust `code`, not just the HTTP status. |
| `message` | string | Localized human text; empty on success. |
| `data` | any | Payload. **Absent entirely** (not `null`) for void operations. |
| `requestId` | string (uuid) | Same value as the `shionlib-request-id` response header. |
| `timestamp` | string | Server time, RFC 3339 UTC with milliseconds. |

* Keys inside `data` are `snake_case` (`title_jp`, `release_date`); envelope and pagination `meta`
  keys are `camelCase` (`totalItems`, `currentPage`). This inconsistency is real.
* Times are always UTC with exactly three fractional digits, `YYYY-MM-DDTHH:MM:SS.sssZ`.
* Release dates are UTC **midnight** (`2004-04-28T00:00:00.000Z`) and mean a calendar date, not an
  instant — do not timezone-shift them.

### 1.2 Auth

* Public reads need **no** authentication, no cookie, no CSRF token, no `Origin`, no `Referer`.
  Verified: identical 200 responses with a browser UA, with `curl/8.x`'s default UA, and with no
  `Accept` header at all.
* Optional auth (only for user-specific data such as favorites): `Authorization: Bearer <jwt>` or
  the `shionlib_access_token` cookie. Refresh token lives in `shionlib_refresh_token`.
* The separate **Open API** (`/api/open/v1/*`) needs an API key `shl_…` sent as
  `Authorization: Bearer shl_…` (see 3.8).
* No Cloudflare challenge was ever triggered on `/api/*` — `cf-cache-status: DYNAMIC`, `Server:
  cloudflare`, plain JSON. The PoW captcha (`/cap/b8809a9aca/`, Cap.js) is used only by login flows.

### 1.3 Rate limits

Per-route, per-IP, exposed on **every** response:

```
x-ratelimit-limit: 120      # observed on /api/search
x-ratelimit-remaining: 118
x-ratelimit-reset: 42       # seconds until the window resets
```

Observed limits: `/api/search` = **120/min**; `/api/game/*`, `/api/game/list`, `/api/tags`,
`/api/settings/public` = **600/min**. On exceeding it the server answers `429` with `Retry-After`
(documented in the repo; **not** reproduced here). A plugin doing 2 requests per game stays far
below both limits, but honour `x-ratelimit-remaining` and back off on 429.

### 1.4 Pagination

Offset pagination, query `page` (1-based, default 1) and `pageSize` (default 10).

| endpoint | `pageSize` max |
|---|---|
| `/api/search` | **50** (`51` → HTTP 422) |
| `/api/game/list` | **100** |

`/api/game/list` returns a `meta` block:

```json
"meta":{"totalItems":3219,"itemCount":1,"itemsPerPage":1,"totalPages":3219,"currentPage":1,"content_limit":0}
```

`/api/search` does **not** return `meta`; each section carries only `{items, total}`.

### 1.5 Transport

`TLS 1.2` accepted (`curl --tlsv1.2 --tls-max 1.2` → 200), HTTP/1.1 accepted (no HTTP/2 needed).
gzip/deflate supported. No `ETag`/`Last-Modified` caching on API responses.

---

## 2. Endpoint summary (all verified live)

| Method | Path | Purpose | Auth |
|---|---|---|---|
| GET | `/api/search` | keyword search → games / developers / characters | none |
| GET | `/api/game/{id}/header` | title block: names, aliases, covers, devs, release date, platforms, external ids | none |
| GET | `/api/game/{id}/details` | description block: intros, tags, screenshots, staff, links, relations, nsfw | none |
| GET | `/api/game/{id}` | merged basic object incl. characters (**no** release date) | none |
| GET | `/api/game/{id}/characters` | character credits | none |
| GET | `/api/game/list` | browse/filter/sort the catalogue | none |
| GET | `/api/tags` | tag search (autocomplete) | none |
| GET | `/api/search/suggest` | title prefix completion | none |
| GET | `/api/settings/public` | `{allow_register, sponsor_enabled}` | none |
| GET | `/api/open/v1/games` | batch lookup by shionlib/VNDB/Bangumi/Hikarinagi id | **API key** |

**Recommended for the plugin: `/api/search` (1 call) + `/api/game/{id}/header` + `/api/game/{id}/details`
(2 calls per game).** `/api/game/{id}` is redundant for metadata (it lacks `release_date`,
`platform`, `v_id`/`b_id`/`h_id`, screenshots and links).

---

## 3. Endpoints in detail

### 3.1 `GET /api/search` — the search to use

```
GET https://shionlib.com/api/search?q=clannad&pageSize=10&page=1
```

Query parameters:

| name | required | notes |
|---|---|---|
| `q` | no (empty → 0 results) | keyword, **max 100 chars**; 101 → HTTP 422 `code 100101`. Matches JP / ZH / EN titles, aliases and romaji. |
| `page` | no | 1-based, default 1 |
| `pageSize` | no | default 10, **max 50**, `51` → 422 |
| `types` | no | repeatable: `types=game&types=developer&types=character`; omitted = `game` only |
| `resources` | no | `any` \| `required`. Omitted = the viewer preference; **anonymous default behaves like `required`** (only games that have a downloadable resource). |

`q` special syntax (verified): `vid:17`, `bid:13`, `hid:9` and a bare VNDB id `v17` resolve to that
exact game (`total:1`). This is the way to look a game up by external id.

Sample request:

```
curl -g -s -A "Mozilla/5.0" "https://shionlib.com/api/search?q=clannad&pageSize=1&page=1"
```

Trimmed sample response (HTTP 200, 1 653 bytes for `pageSize=1`; real values, arrays shortened):

```json
{"code":0,"message":"","data":{
  "games":{"items":[{
    "id":331,"views":6057,
    "title_jp":"CLANNAD","title_zh":"","title_en":"",
    "aliases":["クラナド","团子大家族","小镇家族"],
    "type":"恋爱ADV",
    "covers":[
      {"language":"unknown","type":"dig","url":"https://images.yurari.moe/galgame/10380/29nm9759_2.jpg",
       "dims":[600,900],"thumb_hash":"8EgKFQjriICKWFiIhnm3VkNvSvan","sexual":0,"violence":0,"masked":false},
      {"language":"unknown","type":"dig","url":"https://images.yurari.moe/images/7141fa9b-876e-4eee-b4cc-459a1a2cbe48.webp",
       "dims":[375,640],"thumb_hash":"L2kKDAjDCYl4eHzHaHcrb5n1lQ==","sexual":0,"violence":0,"masked":false}],
    "intro_jp":"","intro_zh":"","intro_en":"",
    "release_date":"2004-04-28T00:00:00.000Z",
    "developers":[
      {"role":"开发","developer":{"id":3279,"name":"Key","aliases":["キー","键社","ビジュアルアーツ/Key","Visual Art`s/Key"]}},
      {"role":"开发","developer":{"id":3535,"name":"株式会社ビジュアルアーツ","aliases":["VISUAL ARTS","视艺动漫有限公司"]}}],
    "resources":{"count":1,"downloads":364,"platforms":["win"],"languages":["en","jp","zh"],
                 "latest":{"at":"2025-12-24T10:57:15.481Z","size":4220542579,"platforms":["win"],"languages":["zh","en","jp"]}},
    "tags":[{"id":19,"name":"汉化"},{"id":85,"name":"全年龄"},{"id":16452,"name":"STEAM"}]
  }],"total":43},
  "developers":{"items":[],"total":0},
  "characters":{"items":[],"total":0}
},"requestId":"513614c2-d3f7-4529-b884-98e095ef725d","timestamp":"2026-10-25T10:34:15.614Z"}
```

Field meanings (search item == `/api/game/list` item, i.e. `GameListItemDTO`):

| field | meaning |
|---|---|
| `id` | **Shionlib game id** — the id used by every other endpoint and by the web page. |
| `views` | page-view counter (int). |
| `title_jp` / `title_zh` / `title_en` | Titles per language. **Any of them may be `""`** — must fall back. |
| `aliases` | Alternate titles/romaji (array, may be `[]`). |
| `type` | Free-text genre label as the source catalog gives it, e.g. `恋爱ADV`, `AVG`, `ADV`, `青春バンドＡＤＶ`. Not an enum. |
| `covers` | 1–2 covers, best first. `type` ∈ `dig` \| `pkgfront`; `language` ∈ `jp`/`zh`/`en`/`unknown`; `dims` = `[w,h]` (may be `[]`); `thumb_hash` = ThumbHash (base64, for a blur placeholder — not a URL); `sexual`/`violence` = content ratings 0..2; `masked` = true when `url` is the blurred copy (see 4). |
| `intro_*` | **Always empty in search/list results** (`""`). Descriptions only come from the details endpoints. |
| `release_date` | ISO instant at UTC midnight, or `null`. |
| `developers` | `[{role, developer:{id,name,aliases[]}}]`; `role` ∈ `开发` (developer), `发行` (publisher), `本地化` (localizer). |
| `resources` | Downloadable-resource summary: `count`, `downloads`, `platforms[]`, `languages[]` (values like `jp`,`zh`,`zh-hant`,`en`), `latest` (newest release `{at,size,platforms,languages}` or `null`). `size` is bytes (int64). |
| `tags` | **At most 3** "distinctive" tags `{id,name}` (generic tags such as `PC`, `Galgame`, `ADV`, and the release-year tag are deliberately excluded). Full tag list comes from `/details`. |

Notes / gotchas:

* **`resources=any` returns a wrong `total`.** `q=clannad&resources=any` → `total:16850` while the
  items are correct (43-ish). Without `resources` (or with `required`) → `total:43`. So: either omit
  `resources` and trust `total`, or pass `resources=any` and ignore `total`, stopping when `items`
  is empty.
* Empty `q` or a missing `q` returns `200` with `items:[] , total:0` (no error) — validate client-side.
* Anonymous users only see games that have at least one downloadable resource by default; that is
  why `q=clannad` gives 43 hits instead of the ~90 CLANNAD entries that exist.

### 3.2 `GET /api/game/{id}/header` — title/header block

```
GET https://shionlib.com/api/game/331/header
```

Full sample response (HTTP 200, 1 381 bytes, verbatim except the second cover's URL):

```json
{"code":0,"message":"","data":{
  "id":331,
  "v_id":"v4",
  "b_id":"13",
  "h_id":371,
  "extra_info":[],
  "title_jp":"CLANNAD","title_zh":"","title_en":"",
  "aliases":["クラナド","团子大家族","小镇家族"],
  "covers":[
    {"language":"unknown","type":"dig","url":"https://images.yurari.moe/galgame/10380/29nm9759_2.jpg","dims":[600,900],
     "thumb_hash":"8EgKFQjriICKWFiIhnm3VkNvSvan","sexual":0,"violence":0,"masked":false},
    {"language":"unknown","type":"dig","url":"https://images.yurari.moe/images/7141fa9b-876e-4eee-b4cc-459a1a2cbe48.webp","dims":[375,640],
     "thumb_hash":"L2kKDAjDCYl4eHzHaHcrb5n1lQ==","sexual":0,"violence":0,"masked":false}],
  "developers":[
    {"role":"开发","developer":{"id":3279,"name":"Key","aliases":["キー","键社","ビジュアルアーツ/Key","Visual Art`s/Key"]}},
    {"role":"开发","developer":{"id":3535,"name":"株式会社ビジュアルアーツ","aliases":["VISUAL ARTS","视艺动漫有限公司"]}}],
  "release_date":"2004-04-28T00:00:00.000Z",
  "release_date_tba":false,
  "type":"恋爱ADV",
  "platform":["win","swi","ps4","ps3","ps2","psv","psp","xb3","and","ios","mob","vnd"],
  "content_limit":0
},"requestId":"33617af6-1540-46cd-b6a8-ec70718fd0b9","timestamp":"2026-10-25T10:34:11.132Z"}
```

| field | meaning |
|---|---|
| `v_id` | **VNDB id**, string, usually already prefixed (`"v4"`, `"v7983"`, `"v21818"`), may be `"4"`-style or `null`. Normalize: `v_id.StartsWith("v") ? v_id : "v"+v_id`. Link → `https://vndb.org/{v_id}`. |
| `b_id` | **Bangumi subject id**, *string* (e.g. `"13"`, `"512739"`) or `null`. Link → `https://bgm.tv/subject/{b_id}`. |
| `h_id` | **Hikarinagi id**, int or `null`. Link → `https://www.hikarinagi.org/galgames/{h_id}` (path from `apps/web/app/shared/catalog/hikarinagi.ts`: `galgame → galgames`). |
| `extra_info` | `[{key,value}]`, observed `[]` on all sampled games — reserved. |
| `release_date` | ISO UTC-midnight instant or `null`; `release_date_tba:true` means announced-but-undated. |
| `platform` | Platform codes, may be `null`/`[]`. Canonical set the download system uses (`apps/api/.../download.go:51`): `win, mac, ios, and, lin, ps3, ps4, psv, psp, swi, dvd`. Values coming from the source catalogue also occur: `ps2, xb3, mob, vnd` (VNDB-style). |
| `content_limit` | Viewer content preference echoed back: 0 = guest, 1 = never show, 2 = show spoiler, 3 = just show. `0` for anonymous callers. |
| `covers`, `developers`, `type` | Same shapes as in 3.1. |

*Non-existent id*: `/api/game/99999999/details` → **HTTP 404**,
`{"code":400101,"message":"游戏不存在","data":null,...}`.

### 3.3 `GET /api/game/{id}/details` — description block

```
GET https://shionlib.com/api/game/331/details
```

Trimmed sample response (HTTP 200, 29 332 bytes; strings shortened, arrays cut to 1–2 elements,
all key names and value types verbatim):

```json
{"code":0,"message":"","data":{
  "id":331,
  "extra_info":[],
  "tags":[{"tag_alias":null,"tag":{"id":16445,"name":"Galgame","aliases":[],"count":10956}},
          {"tag_alias":null,"tag":{"id":16443,"name":"ADV","aliases":[],"count":9472}}],
  "intro_jp":"フツーの生活を送っていた主人公、岡崎朋也。…",
  "intro_zh":"",
  "intro_en":"Do you like this school?\" she suddenly asks, …",
  "images":[
    {"url":"https://images.yurari.moe/galgame/10380/i33rjpqi_2.jpg","dims":[800,600],"thumb_hash":"MRgGFYRKcITqeIpohpeWiQpSW6D4","sexual":0,"violence":0},
    {"url":"https://images.yurari.moe/galgame/10380/dg4tu1eb_2.jpg","dims":[800,600],"thumb_hash":"5AcKDYSKmId/eYd3d5d4itBZFgBW","sexual":0,"violence":0}],
  "staffs":[{"name":"麻枝 准","role":"剧本"},{"name":"樋上 いたる","role":"原画"}],
  "nsfw":false,
  "link":[{"id":236509,"name":"website","label":"Official website","url":"http://key.visualarts.gr.jp/product/clannad/"},
          {"id":236531,"name":"steam","label":"Steam","url":"https://store.steampowered.com/app/324160/"}],
  "relations_from":[{"id":28632,"relation":"SEQUEL","to_game_id":332,"to_game":{"id":332,"title_jp":"智代アフター 〜It’s a Wonderful Life〜","title_zh":"智代After ～It's a Wonderful Life～","title_en":"Tomoyo After ~It's a Wonderful Life~","aliases":["…"],"type":"恋愛ADV","covers":[{"language":"unknown","type":"dig","url":"https://images.yurari.moe/images/55b98aca-b876-4533-8d80-0a4febc8c0d5.webp","dims":[640,1066],"thumb_hash":"9AcGDALINof3do93e5kvme/yiQ==","sexual":0,"violence":0,"masked":false}],"intro_jp":"…","intro_zh":"…","intro_en":"…","release_date":"2005-11-25T00:00:00.000Z","developers":[{"role":"开发","developer":{"id":3279,"name":"Key","aliases":["キー"]}}],"resources":{"count":1,"downloads":77,"platforms":["win"],"languages":["zh"],"latest":{"at":"2025-12-24T10:58:31.234Z","size":1930253483,"platforms":["win"],"languages":["zh"]}},"tags":[{"id":19,"name":"汉化"}]}}],
  "content_limit":0
},"requestId":"d9c51c1a-3b24-4bde-8370-cfef9662ef92","timestamp":"2026-10-25T10:34:10.111Z"}
```

| field | meaning |
|---|---|
| `intro_jp` / `intro_zh` / `intro_en` | **Plain text** with `\n` line breaks (no HTML/`<br>` observed). Any of them may be `""`. |
| `tags` | Full tag list `[{tag_alias, tag:{id,name,aliases[],count}}]` (29 tags for CLANNAD). `tag_alias` is a per-game override label, usually `null`. |
| `images` | Screenshots: `url` (absolute), `dims` `[w,h]`, `thumb_hash`, `sexual`, `violence`. **Key present but value `null`** when the game has none (e.g. game `10604`) — and it is omitted for viewers who hide rated content. Always null-check. |
| `staffs` | `[{name, role}]` credits; `role` free text (`剧本`, `原画`, `音乐`, `人物设定`, `主题歌演出`, `翻译`, `QC`, …), sometimes `""`. |
| `nsfw` | bool — the game is flagged adult in the catalogue. **Independent of `covers[].sexual`/`masked`**: game `10604` has `nsfw:false` but its only cover has `sexual:1, masked:true`. |
| `link` | Official/retail/store links `[{id,name,label,url}]`; `name` is a machine key (`website`, `steam`, `getchu`, `dmm`, `nintendo`, `playstation_jp`, `jlist`, `googplay`, `renai`, …), `label` is the display text. |
| `relations_from` | Relations **from** this game: `[{id, relation, to_game_id, to_game:<GameCardDTO>}]`. `relation` values observed: `SEQUEL`, `PREQUEL`, `SIDE_STORY`, `SAME_UNIVERSE`. There is **no** `relations_to` field in the DTO (incoming relations are not returned). |
| `content_limit` | As in 3.2. |

### 3.4 `GET /api/game/{id}` — merged basic object (optional)

HTTP 200, 5 863 bytes for game `10604`. Contains `title_*`, `intro_*`, `covers`, `developers`,
`characters`, `staffs`, `tags` (`[{tag_alias,tag{...}}]`), `content_limit` — **no** `release_date`,
`platform`, `v_id`/`b_id`/`h_id`, `images`, `link` or `relations`. Use it only if you need the
character cast; otherwise `/header` + `/details` is strictly better.

### 3.5 `GET /api/game/{id}/characters` (optional)

`{"code":0,...,"data":{"characters":[{"role":"main","image":"https://images.yurari.moe/images/<uuid>.webp",
"image_thumb_hash":"…","actor":"能登麻美子","character":{"id":110065,"image":"…","image_thumb_hash":"…",
"name_jp":"一ノ瀬 ことみ","name_zh":"一之濑琴美","name_en":"Ichinose Kotomi","aliases":["…"],
"intro_jp":"…","intro_zh":"…","intro_en":"…","gender":["f"],"blood_type":"a","height":160,"weight":null,
"bust":null,"waist":null,"hips":null,"cup":null,"age":null,"birthday":[]}}]}}` — 43 658 bytes for CLANNAD.

### 3.6 `GET /api/game/list` — browse/filter (optional)

```
GET https://shionlib.com/api/game/list?page=1&pageSize=100&filter[tags][]=Galgame&filter[sort_by]=views
```

Remember `-g` for curl (URL globbing eats `[...]`); in C# `Uri.EscapeDataString` the brackets or
just append them raw — the server accepts both.

| param | values |
|---|---|
| `page`, `pageSize` | `pageSize` ≤ **100** |
| `filter[tags][]` | tag *names*; repeatable, "any of" |
| `filter[exclude_tags][]` | tag names, "none of" |
| `filter[years][]` | 1900–2100, repeatable |
| `filter[months][]` | 1–12 |
| `filter[platforms][]` | platform codes |
| `filter[sort_by]` | `release_date` (default) \| `views` \| `downloads` \| `hot_score` \| `resource_updated` |
| `filter[sort_order]` | `asc` \| `desc` (default `desc`) |
| `filter[start_date]`, `filter[end_date]` | ISO 8601 |
| `resources` | `any` \| `required` |
| `developer_id`, `character_id`, `translator_id` | int |

Response item = `GameListItemDTO` (same as 3.1) plus the `meta` block of 1.4.
Verified: `filter[tags][]=Galgame` → `totalItems:3219`; `filter[years][]=2004` → `totalItems:129`,
first item `release_date:2004-01-15T00:00:00.000Z`.

### 3.7 Small helpers (optional)

* `GET /api/tags?q=幽默&limit=3` → `{"code":0,...,"data":[{"id":..,"name":"幽默","count":..,"aliases":[],"display_name":"幽默"}]}` (`limit` ≤ 100; `display_name` was identical to `name` for `lang=zh/en/ja`).
* `GET /api/search/suggest?prefix=cl&limit=10` → `data: ["CLANNAD", …]` (plain string array, `limit` ≤ 20, `prefix` required).
* `GET /api/search/trending` → trending keywords.
* `GET /api/settings/public` → `{"code":0,...,"data":{"allow_register":true,"sponsor_enabled":true}}` (168 bytes).

### 3.8 `GET /api/open/v1/games` — Open API (needs key)

```
GET https://shionlib.com/api/open/v1/games?id=331,10604&vid=17&bid=13&hid=9
Authorization: Bearer shl_xxxxxxxx
```

* Params `id` / `hid` / `bid` / `vid`: comma-separated, **up to 50 ids each** (the batch form the
  public endpoints lack), `vid` leading `v` optional.
* Response `data` = `{games:[OpenGameDTO], missing:["…"]}` where `OpenGameDTO` =
  `{id, hid, bid, vid, title_jp, title_zh, title_en, nsfw, url, resources:[OpenResourceDTO]}`.
  It is a *lookup* payload — no intros, covers, tags or screenshots — so it is **not** a substitute
  for `/header` + `/details`; it is useful only for id cross-mapping.
* Without a key: **HTTP 401**
  `{"code":680101,"message":"请在 Authorization 请求头中附上 API 密钥","data":null,...}`
  (verified). `/api/open/games` without `v1` → 404.
* Keys are created in the user settings page `/zh/user/settings/api-keys` and look like `shl_…`
  (`components.securitySchemes.apiKey`: `{"type":"http","scheme":"bearer","description":"An API key
  of the form shl_…"}`).
* **[GUESS — do not rely on]** Whether the Open API is currently enabled: the site's own i18n
  strings contain "开放接口已暂停服务，API 密钥暂时无法使用" ("the Open API is suspended, API keys
  cannot be used for now"). We could not test with a key. Design the plugin to work **without** it
  (the public `/api/*` endpoints above cover everything) and treat a key as optional sugar.

---

## 4. Images

Two URL families appear in `covers[].url` and `images[].url`; both are absolute, no auth needed:

```
https://images.yurari.moe/galgame/<dirId>/<hash>_2.jpg      # legacy VNDB-derived art, dirId is NOT the game id (CLANNAD id 331 → dir 10380)
https://images.yurari.moe/images/<uuid>.webp                # modern object-store art
```

HTTP 200, `Content-Type: image/jpeg|image/webp`. Hosts from the site config: bucket
`https://images.yurari.moe`, processor `https://imagesp.yurari.moe`. **`https://t.shionlib.com`
(publicConfig `imageBaseUrl`) is NOT an image host** — `/galgame/...` and `/images/...` under it
return 404; do not use it.

### 4.1 Resizing (usable, verified)

`imagesp.yurari.moe` is an on-the-fly image processor; same path, query params:

```
https://imagesp.yurari.moe/galgame/10380/29nm9759_2.jpg?w=600&f=webp&q=80   → 200, image/webp, 59 152 B
https://imagesp.yurari.moe/galgame/10380/29nm9759_2.jpg?w=200&f=webp        → 200, image/webp, 14 592 B
https://imagesp.yurari.moe/galgame/10380/29nm9759_2.jpg                    → 200, image/jpeg, 92 322 B
```

Params seen: `w`, `h` [GUESS, not exercised], `q` (quality), `f` (`webp`), `blur` (px).
Exploiting this is optional — Playnite downloads whatever URL you give it.

### 4.2 `masked` covers and the blur

The backend's "masker" (`apps/api/.../masker.go`) rewrites adult covers for guests as
`processorURL + "/" + key + "?w=320&blur=48&q=60&f=webp"` and sets `masked:true`
(`const maskedParams = "w=320&blur=48&q=60&f=webp"`). Guests get `content_limit = 0`
(`ContentLimitGuest`), so **every anonymous plugin call gets blurred art for adult games**, e.g.
game `10604`:

```
"url":"https://imagesp.yurari.moe/images/eb4dd578-55de-468e-a9a7-3bcc0a286437.webp?w=320&blur=48&q=60&f=webp",
"masked":true,"sexual":1
```

Two consequences:

* The `url` as returned is a **320 px-wide blurred thumbnail** — poor quality for a Playnite cover.
* The mapping is trivially reversible (verified by HTTP 200 + 20 992 bytes): drop the query string
  and the host gives the original asset —
  `https://images.yurari.moe/images/eb4dd578-....webp` (bucket) or
  `https://imagesp.yurari.moe/images/eb4dd578-....webp` (no query) both serve the unblurred file.
  This is **undocumented behaviour**; if you use it, make it an explicit user setting
  ("use uncensored/unsafe Covers") and keep the default to what the API returned.
* The only *documented* way to unblur is an authenticated session with `content_limit` = 3 — i.e.
  pasting the user's `shionlib_access_token` cookie into plugin settings. Not recommended.

### 4.3 `thumb_hash`

`thumb_hash` is a **ThumbHash** (base64, e.g. `"8EgKFQjriICKWFiIhnm3VkNvSvan"`; occasionally with
`/`,`+`,`=` padding as in `"L2kKDAjDCYl4eHzHaHcrb5n1lQ=="`). It is a ~20-byte placeholder, not a URL.
Useful only if you decode ThumbHash yourself to synthesise a 32×32 placeholder; otherwise ignore.
`dims` is `[width,height]` and may be `[]` when unknown.

---

## 5. Human-facing page URL

```
https://shionlib.com/zh/game/{id}      → 200
https://shionlib.com/en/game/{id}      → 200
https://shionlib.com/ja/game/{id}      → 200
https://shionlib.com/game/{id}         → 301 → https://shionlib.com/zh/game/{id}
https://shionlib.com/zh/game/99999999  → 404
```

Store `https://shionlib.com/zh/game/{id}` as the Playnite `Game.Links` entry / `StoreUrl`.
Other public pages: `/zh/developer/{id}` [GUESS], `/zh/character/{id}` [GUESS].

---

## 6. Error reference (observed)

| HTTP | `code` | `message` | cause |
|---|---|---|---|
| 404 | `400101` | `游戏不存在` | unknown game id |
| 422 | `100101` | `参数校验失败` | validation; `data` = `{"errors":[{"field":"q","messages":["q 必须小于 100 个字符"]}]}` |
| 401 | `680101` | `请在 Authorization 请求头中附上 API 密钥` | `/api/open/v1/*` without key |
| 404 | `404` | `未找到资源` | unknown route (e.g. `/api/games`, `/api/v1/search`) |
| 429 | — | — | rate limit; honour `Retry-After` (documented, not reproduced) |

Codes are stable per-operation; the repo keeps the full list in `apps/api/docs/business-codes.md`.
Business codes are *not* HTTP codes (e.g. `400101` on a 404), so always branch on `code`.

---

## 7. C# implementation notes (.NET Framework 4.6.2 / Playnite)

### 7.1 HTTP client setup

```csharp
// .NET 4.6.2 does NOT default to TLS 1.2 — Cloudflare will fail the handshake without this.
ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
ServicePointManager.Expect100Continue = false;
ServicePointManager.DefaultConnectionLimit = 8;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("PlayniteShionlibPlugin/0.1 (+https://shionlib.com)");
http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
// Optional: http.DefaultRequestHeaders.AcceptEncoding (gzip) + HttpClientHandler.AutomaticDecompression
```

* Keep **one** static `HttpClient` (Playnite keeps plugins loaded for the whole session).
* No cookie container, no `Referer`/`Origin`, no CSRF header — none are required (verified with a
  bare `curl`).
* `application/json` on the request is optional; the server always answers JSON.
* Build query strings with `Uri.EscapeDataString(q)` (CJK names, spaces, `&` must be escaped).
  Brackets in `filter[tags][]` do not need escaping.

### 7.2 Two-call fetch per game

```csharp
GET {base}/api/game/{id}/header    → names, aliases, covers, developers, release date, platforms, v_id/b_id/h_id
GET {base}/api/game/{id}/details   → intros, tags, screenshots, staff, links, relations, nsfw
GET {base}/api/search?q={q}&pageSize={n}&page={p}   → id + display name + cover for the search window
```

Playnite contract: `GetMetadataSearchResults` returns `GenericItem`-like results carrying the id in
`GameId`/`ItemId` (string), the display name and the cover URL; `GetMetadata(gameId)` fills a
`GameMetadata`. Cache the id → nothing else is needed, because `/api/game/{id}` re-derives everything.

### 7.3 Classes sketch

Minimal, with `Newtonsoft.Json` (`[JsonObject(MemberSerialization.OptIn)]`, `NullValueHandling.Ignore`):

```csharp
class ApiEnvelope<T> { public int code; public string message; public T data; public string requestId; public string timestamp; }

class SearchRoot      { public SearchSection games, developers, characters; }
class SearchSection   { public List<GameListItem> items; public long total; }

class GameListItem {
  public long id; public long views;
  public string title_jp, title_zh, title_en, type;
  public List<string> aliases;
  public List<CoverDTO> covers;
  public string intro_jp, intro_zh, intro_en;
  public DateTime? release_date;
  public List<CreditDTO> developers;
  public ResourceSummary resources;
  public List<TagRef> tags;
}

class CoverDTO    { public string language, type, url, thumb_hash; public long[] dims; public int sexual, violence; public bool masked; }
class CreditDTO   { public string role; public DeveloperRef developer; }
class DeveloperRef{ public long id; public string name; public List<string> aliases; }
class ResourceSummary { public long count, downloads; public List<string> platforms, languages; public LatestResource latest; }
class LatestResource  { public DateTime at; public long size; public List<string> platforms, languages; }
class TagRef      { public long id; public string name; }

class GameHeader {
  public long id; public string v_id, b_id; public long? h_id;
  public List<ExtraInfo> extra_info;
  public string title_jp, title_zh, title_en, type;
  public List<string> aliases; public List<CoverDTO> covers; public List<CreditDTO> developers;
  public DateTime? release_date; public bool release_date_tba;
  public List<string> platform; public int content_limit;
}

class GameDetails {
  public long id; public List<ExtraInfo> extra_info; public List<TagLink> tags;
  public string intro_jp, intro_zh, intro_en;
  public List<CoverDTO> images;          // may be null
  public List<Staff> staffs; public bool nsfw;
  public List<LinkDTO> link;
  public List<Relation> relations_from;
  public int content_limit;
}
class TagLink   { public string tag_alias; public Tag tag; }      // tag = {id,name,aliases,count}
class Staff     { public string name, role; }
class LinkDTO   { public long id; public string name, label, url; }
class Relation  { public long id, to_game_id; public string relation; public GameCard to_game; }
```

Prefer `JObject`/`JToken` parsing if you want maximum resilience: dozens of fields are
`null`-or-`""`-or-`{}` depending on the game (see 8). With `DataContractJsonSerializer` (no
dependency) you must decorate everything with `[DataMember(Name="title_jp")]` and handle `null`
arrays by hand.

### 7.4 Field mapping to Playnite's `GameMetadata`

| Playnite | Shionlib source | rule |
|---|---|---|
| `Name` | `header.title_zh` → `title_en` → `title_jp` (locale order per plugin setting) | first non-empty (`!string.IsNullOrWhiteSpace`) |
| `AlternateNames` | `header.aliases` + the two other titles | dedupe, keep non-empty |
| `Description` | `details.intro_zh` → `intro_en` → `intro_jp` | plain text; Playnite renders HTML, so `HtmlEncode` then replace `\n` with `<br>` (or keep raw — `\n` is fine in Playnite's description box) |
| `ReleaseDate` | `header.release_date` | `DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal \| DateTimeStyles.AssumeUniversal, out dt)` then use **`dt.Date` only** (Year/Month/Day). Do not `ToLocalTime()` — UTC-midnight can shift the day. `release_date_tba == true` or `null` → leave empty. |
| `Developers` | `header.developers` where `role == "开发"` | `developer.name` |
| `Publishers` | `header.developers` where `role == "发行"` or `"本地化"` | `developer.name`; may be empty |
| `Genres` | `header.type` | free text (`恋爱ADV`); optionally split, else put it in `Tags` |
| `Tags` | `details.tags[].tag.name` (+ `tag.aliases`) | full list; skip the noise tags `PC`, `Windows`, `Galgame`, `ADV`, `AVG`, `游戏` if you want cleaner tags (that is exactly what the search/list DTO already does) |
| `Platforms` | `header.platform` | map: `win→PC (Windows)`, `lin→PC (Linux)`, `mac→Mac`, `and→Android`, `ios→iOS`, `swi→Nintendo Switch`, `ps3/ps4/psv/psp/ps2→Sony PlayStation`, `xb3→Xbox 360`, `dvd`, `mob`, `vnd` → `PC`; unknown → `PC` |
| `CoverImage` | `header.covers[0].url` (or the `type=="dig"` one, largest `dims`) | absolute URL; Playnite downloads it. Respect `masked` (see 4.2) |
| `BackgroundImage` | `details.images[0].url` (largest `dims`) | screenshots |
| `Screenshots` *(extra metadata / `GameMetadata` via `Screenshot`)* | `details.images[].url` | Playnite's `GameMetadata` has no public screenshot collection — download them into the game's `Screenshots` folder or expose them as links (see caveat) |
| `Links` | `https://shionlib.com/zh/game/{id}`, `https://vndb.org/{v_id}`, `https://bgm.tv/subject/{b_id}`, `https://www.hikarinagi.org/galgames/{h_id}`, plus `details.link[]` (`label`/`url`) | dedupe by URL |
| `CommunityScore` | — | not exposed by the API (`/api/game/score/vndb/{id}` and `/api/game/score/bangumi/{id}` exist but are per-source; [GUESS] they return the external score) |

### 7.5 Caveats checklist

1. **Titles are sparse.** `title_en` is `""` for most Japanese games; `title_zh` for many doujin
   titles. Always fall back; never assume a title is non-empty.
2. **`intro_*` are `""` in search results** — you must call `/details` for a description.
3. **`images` may be JSON `null`** (no screenshots, or rated content hidden). Same for `platform`,
   `aliases`, `relations_from`, `extra_info`, `latest`.
4. **`release_date` is UTC midnight** — take the date part only.
5. **`b_id` is a string, `h_id`/`v_id` are string/int/null** — don't cast blindly.
6. **Search results only contain games with downloadable resources** by default; pass
   `resources=any` to widen it, but then `total` is garbage (see 3.1).
7. **`pageSize` caps differ** (search 50, list 100); `51` on search is a 422, not a silent clamp.
8. **Rate limits** 120/min (search) and 600/min (details) per IP; batch search results and cache
   detail responses for the session.
9. **Adult art is blurred for guests** — the `url` you get is a 320 px blurred copy for such games.
10. **Id namespaces**: the `id` in JSON is the Shionlib game id used by the web page and every
    endpoint. It is *unrelated* to the `10380`-style directory id in legacy
    `/galgame/{dirId}/…jpg` image paths — never derive one from the other.
11. **Encoding**: responses are UTF-8 **without BOM**; read with `Encoding.UTF8`. On a GBK
    PowerShell console the JSON looks like mojibake — that is a console artefact, not the API.
12. **`curl` users**: pass `-g`, otherwise `filter[tags][]=…` is treated as URL globbing and the
    request silently returns an empty body.

---

## 8. Verification log (what was actually executed)

| # | request | result |
|---|---|---|
| 1 | `GET /api/search?q=clannad` | 200, 11 555 B, `total:43` |
| 2 | `GET /api/search?q=clannad&pageSize=1&page=1` | 200, 1 653 B |
| 3 | `GET /api/search?q=%E5%8D%83%E6%81%8B%E4%B8%87%E8%8A%B1` | 200, 10 items, first `千恋＊万花` |
| 4 | `GET /api/search?q=Symphonic%20Rain` | 200, 10 items, first `シンフォニック=レイン` |
| 5 | `GET /api/search?q=vid%3A17` / `q=v17` / `q=bid%3A13` | 200, `total:1` each (Ever17 / CLANNAD) |
| 6 | `GET /api/search?q=` and `GET /api/search` | 200, `items:[] total:0` |
| 7 | `GET /api/search?q=clannad&pageSize=51` | **422**, `code 100101` |
| 8 | `GET /api/search?q=<101 chars>` | **422**, `messages:["q 必须小于 100 个字符"]` |
| 9 | `GET /api/search?q=clannad&resources=any` | 200, `total:16850` (wrong) but correct items |
| 10 | `GET /api/game/331/header` | 200, 1 381 B |
| 11 | `GET /api/game/331/details` | 200, 29 332 B |
| 12 | `GET /api/game/331` | 200, 42 831 B |
| 13 | `GET /api/game/331/characters` | 200, 43 658 B |
| 14 | `GET /api/game/10604/details` and `/header` | 200; `images:null`, `nsfw:false`, masked cover, `v_id`/`b_id`/`h_id` present |
| 15 | `GET /api/game/18237/header` + `/details` | 200; minimal game (`title_jp:""`), 7 screenshots, 2 links |
| 16 | `GET /api/game/99999999/details` | **404**, `code 400101` |
| 17 | `GET /api/game/list?pageSize=1&filter[tags][]=Galgame` | 200, `totalItems:3219` |
| 18 | `GET /api/game/list?pageSize=100` | 200, 100 items, `totalPages:34` |
| 19 | `GET /api/tags?q=幽默&limit=3` | 200, `display_name` identical for `lang=zh|en|ja` |
| 20 | `GET /api/settings/public` | 200, 168 B |
| 21 | `GET /api/open/v1/games?id=331` (no key) | **401**, `code 680101` |
| 22 | `GET /api/v1/game/331`, `/api/games`, `/api/docs`, `/api/openapi.json`, `/api/game/331/screenshots`, `/api/game/331/relations`, `/api/games/331`, `/api/search/games` | **404** `未找到资源` (no such routes) |
| 23 | same request with `curl` default UA / no `Accept` | identical 200 → no bot filter |
| 24 | response headers of 8 endpoints | `x-ratelimit-limit` 120 (search) / 600 (rest) |
| 25 | `curl --tlsv1.2 --tls-max 1.2` and `--http1.1` | 200 both |
| 26 | `/zh/game/331`, `/en/game/331`, `/ja/game/331` | 200; `/game/331` → 301 to `/zh/game/331`; `/zh/game/99999999` → 404 |
| 27 | image hosts: bucket / processor / `t.shionlib.com` | 200 / 200 / **404** |

## 9. Not verified / open

* `429` payload and `Retry-After` (documented in the repo, not reproduced).
* `/api/open/v1/games` with a real `shl_…` key — no key available; the site also advertises that the
  Open API may be suspended.
* Response of `/api/game/score/vndb/{id}`, `/api/game/score/bangumi/{id}`, `/api/search/trending`,
  `/api/activity/list`, `/api/game/random` (all listed in the OpenAPI document, not exercised).
* Whether the site's `/resources/*` SSR/BFF routes expose anything the `/api` layer does not — they
  only feed the React Router loaders.
* Localisation of `message` via `shionlib_locale` cookie / `lang` query / `Accept-Language`
  (documented; only `data` was inspected, and `data` is language-complete by design).

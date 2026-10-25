# TouchGal (www.touchgal.ink) — HTTP API research for a Playnite metadata plugin

Date of testing: 2026-10-25, from a Windows 10 machine in CN (egress IP `183.61.117.225`, Cloudflare `colo=SIN`).
Tools used: `curl.exe` 8.4.0 (Schannel), PowerShell 5.1, Google Chrome 156 headless/headful, Microsoft Edge 156 headless, CDP over WebSocket, unpacked Chromium extensions.

---

## 1. VERDICT (short)

**FEASIBLE — but only through a real, non-instrumented Chromium engine. NOT feasible with `HttpClient`/`WebClient`/`curl`, and NOT feasible with a pasted `cf_clearance` cookie.**

| Client | GET `/` | GET `/api/home` | POST `/api/search` |
|---|---|---|---|
| `curl.exe` (any UA / any headers) | 403 Cloudflare challenge | 403 | 403 |
| `curl.exe` + fresh valid `cf_clearance` + identical UA | 403 (re-challenged) | 403 | 403 |
| Chrome/Edge **with DevTools/CDP attached** | 200 | 200 | **403 CF block page (or hang)** |
| Chrome/Edge **without** DevTools (real page, headful **or** headless) | 200 | 200 | **200 JSON** |

The gate is not the network and not the method — it is *"is this a genuine browser session without automation instrumentation"*.
A Playnite plugin must therefore drive an embedded engine (**WebView2 recommended**, CefSharp as alternative) and `ExecuteScriptAsync` the `fetch()` calls. Verified analogue: `msedge.exe --headless=new` with **no** `--remote-debugging-port` returns `200 {"galgames":[...],"total":1}` for the exact search the real site performs.

Fallback if embedding a browser is unacceptable: **NextMoe open API** (`https://api.nextmoe.dev/v2/catalog`, plain JSON over plain HTTP, no Cloudflare) — verified working, see §8. It is *not* a TouchGal mirror (no TouchGal ids/refs exist in it), it is an alternative metadata source covering the same galgames (vndb/bangumi/steam/dlsite refs, covers, intros, tags, ratings).

---

## 2. What TouchGal is, and which host serves the data

- The site is open source: `https://codeload.github.com/KUN1007/kun-touchgal-next/zip/refs/heads/main` → 200 (4,682,074 B). Local copy: `C:\Users\23\AppData\Local\Temp\galtool\repos\touchgal\touchgal-src\kun-touchgal-next-main` (Next.js App Router, Prisma, optional Meilisearch).
- `www.touchgal.ink` → CNAME `touchgal.alidns-2.com` → Cloudflare A records `172.64.154.172 / 104.18.33.167 / 104.18.33.230 / 172.64.154.32`.
- `touchgal.com` is the zone's canonical name; `www.touchgal.com` → CNAME `cn1.touchgaloss.cfd` → `8.138.248.194` (Aliyun nginx) → **301** `Location: https://www.touchgal.ink`. Apex `touchgal.ink` → curl `schannel: SEC_E_ILLEGAL_MESSAGE` (broken TLS for old Schannel stacks).
- There is **no separate API host**. `api.touchgal.ink` does not resolve. NXDOMAIN for: `api, m, cdn, img, static, tgapi, data, backup, old, new, beta, test, s, ws, wsapi, v2, files, dl, download, pic` (`*.touchgal.ink`).
- Meilisearch is **server-side only** (`MEILISEARCH_HOST = "http://127.0.0.1:7700"` in `.env.example`), never exposed publicly — there is no public search engine to query directly.
- Images/patches come from `https://cloud.touchgaloss.com/...` (**not** behind the challenge — see §7).

---

## 3. Raw-HTTP probing log (all observed with `curl.exe`, Chrome UA, `--compressed`, HTTP/1.1)

Every single one of the following returned **HTTP 403, `text/html`, ~5.6 KB, Cloudflare "Just a moment..." / `_cf_chl_opt` / `Cf-Mitigated: challenge`**:

```
GET /                       GET /api            GET /api/          GET /api/v1
GET /api/v1/galgame         GET /api/galgame    GET /api/search    GET /api/v1/search
GET /robots.txt             GET /sitemap.xml    GET /sitemap_index.xml
GET /rss.xml  /feed  /feed.xml  /atom.xml
GET /favicon.ico   /manifest.json   /_next/data/   /_next/static/chunks/webpack.js
GET /google-site-verification.html   /.well-known/security.txt
GET /404   /api/health   /health   /api/status
```

Variations tried, all still 403: header sets (full browser set incl. `Accept`, `Accept-Language`, `sec-ch-ua`, `sec-ch-ua-mobile`, `sec-ch-ua-platform`, `Sec-Fetch-*`, `Referer: https://www.touchgal.ink/`), UA swaps (curl default, Chrome, Googlebot, bingbot, mobile Safari, `python-requests`, wget, empty), `--http1.1`, `--compressed`, `http://` (port 80 → **301** to https).

Two interesting non-403 results:

- **`GET https://www.touchgal.ink/cdn-cgi/trace` → 200 `text/plain`, 213 B** (bypasses the challenge, but contains no site data):
  `fl=...,h=www.touchgal.ink,ip=183.61.117.225,ts=...,visit_scheme=https,uag=curl/8.4.0,colo=SIN,sliver=none,http=http/1.1,tls=TLSv1.3,sni=plaintext,loc=CN`
- `https://www.touchgal.com/` and `https://touchgal.com/` → **301** to `https://www.touchgal.ink`.

### 3.1 The `cf_clearance` experiment (cookie paste) — FAILED

A real, freshly minted clearance cookie was captured from a Chrome that had just solved the challenge:

```
cf_clearance = <token>-1792924507-1.2.1.1-<...>   domain=.touchgal.ink  httpOnly  expires≈1824460506
UA           = Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/156.0.0.0 Safari/537.36
```

Then:

```
curl.exe -A "<that exact UA>" -b "cf_clearance=<that exact value>" https://www.touchgal.ink/
      -> 403|text/html|5634   ("Just a moment...")
curl.exe ... https://www.touchgal.ink/api/home
      -> 403|5658
curl.exe -X POST ... https://www.touchgal.ink/api/search
      -> 403|5984
```

Cloudflare binds `cf_clearance` to the client TLS fingerprint (JA3/JA4) + UA + IP, so a non-Chromium HTTP stack is re-challenged even with a perfect cookie. **A "paste your cookie" plugin setting cannot work.** `document.cookie` in the browser is also empty (all cookies are httpOnly).

### 3.2 Automation is what gets blocked, not the network

| Experiment | Result |
|---|---|
| Chrome headless, `--remote-debugging-port`, `Runtime.enable`, `fetch('/api/search', POST)` | **403 CF block page, 4546 B** ("Sorry, you have been blocked", no challenge) |
| Chrome headful, same CDP driver | **403** (same 4546 B block page) |
| Chrome headless, same CDP, `POST /api/home` and `POST /api/search`/`PUT` | 403 |
| Chrome headless, same CDP, `POST /` (a *page* route, not `/api`) | **200** — the rule is scoped to non-GET on `/api/*` |
| Chrome headless, **no CDP**, `file://` form POST (cross-site, `Origin: null`) | 403 block page — the cross-site `Origin: null` is itself a rule hit, so this row proves little |
| **Edge headless, no CDP, extension content script, same-origin `fetch`** | **200 `application/json`** |
| Edge headful, no CDP, same extension | **200 `application/json`** |

Conclusion (verified): with a genuine same-origin browser request and no DevTools instrumentation, the whole API answers normally — `POST /api/search` in 0.4–1.1 s, `GET /api/patch` in 0.35 s. With DevTools attached, non-GET on `/api/*` is hard-blocked at the edge.

Reproduction harness that works (kept in the workspace):
- `research\cdp.ps1` — launches Chrome with `--remote-debugging-port`, solves the challenge, evaluates an expression file. **Useful for GET debugging only; its POSTs are always blocked.**
- Unpacked probe extension used for the successful runs: `%TEMP%\galtool\ext\` (simple POST), `ext2\` (multi-endpoint sweep), `ext3\` (chunked full-body capture); launched with
  `msedge.exe --headless=new --user-data-dir=<fresh> --load-extension=<extdir> --enable-logging=stderr --v=0 https://www.touchgal.ink/`
  and results read out of the `INFO:CONSOLE` lines of stderr.
- Note: `chrome.exe` **refuses** `--load-extension` / `--disable-extensions-except` ("not allowed in Google Chrome, ignoring"); `msedge.exe` accepts them.

---

## 4. The real API (verified 200, from the browser channel)

Base: `https://www.touchgal.ink/api`. All responses are `application/json`. The site's own client (`utils/kunFetch.ts`) always sends:

```
X-Requested-With: kun-fetch
credentials: include          (cookies; needed only for user-specific fields)
Content-Type: application/json   (POST bodies)
```

### 4.1 Search by name — `POST /api/search`

```
POST https://www.touchgal.ink/api/search
Content-Type: application/json
X-Requested-With: kun-fetch

{
  "queryString": "[{\"type\":\"keyword\",\"mode\":\"include\",\"name\":\"千恋万花\"}]",
  "limit": 20,
  "searchOption": { "searchInIntroduction": false, "searchInAlias": true, "searchInTag": true },
  "page": 1,
  "selectedType": "all",
  "selectedLanguage": "all",
  "selectedPlatform": "all",
  "sortField": "resource_update_time",
  "sortOrder": "desc",
  "selectedYears": ["all"],
  "selectedMonths": ["all"],
  "minRatingCount": 0
}
```

- `queryString` is a **JSON string** (not an object) of a suggestion array: `[{"type":"keyword"|"tag"|"company","mode":"include"|"exclude","id"?:number,"name":string}]` (max 10 keywords per the tag-search schema; `name` max 107 chars).
- `limit` 1..24, `page` 1..9999999, `minRatingCount` 0..999999 (server default 10 — set 0 or results with few votes vanish).
- `sortField` ∈ `resource_update_time | created | rating | view | download | favorite`; `sortOrder` ∈ `asc | desc`.
- `selectedType`/`selectedLanguage`/`selectedPlatform` accept `"all"` or a value from `ALL_SUPPORTED_TYPE` / `ALL_SUPPORTED_LANGUAGE` / `ALL_SUPPORTED_PLATFORM` (`constants/…`).

Real response for `千恋万花` (complete body, 480 B, `len` matches):

```json
{"galgames":[{"id":637,"uniqueId":"a555829b","name":"千恋＊万花","banner":"https://cloud.touchgaloss.com/patch/637/banner/banner.avif","view":1857552,"download":670919,"type":["other","game","audio","image","fix","adult","save","uncensored","mod"],"language":["zh-Hans","other","ja"],"platform":["windows","emulator","apk","other","macos","linux","ipa"],"created":"2023-06-08T19:03:32.000Z","_count":{"favorite_folder":5549,"resource":59,"comment":375},"averageRating":8.4}],"total":1}
```

`POST /api/search` with `"Summer"` → 200, 6,990 B, several hits, same card shape, e.g.
`{"id":11234,"uniqueId":"1a82c986","name":"_summer",... "averageRating":5}`.

- `GET /api/search` → **405** (the route only exports POST).
- Auth is not required for search; `getPatchVisibilityContext` only filters NSFW/blocked tags.

### 4.2 Details by id — `GET /api/patch?uniqueId=<8 chars>`

The "id" a plugin must fetch with is the **8-character `uniqueId`**, not the numeric `id` (the `patchId` parameter of this route is only used by `DELETE`). Every search/list result carries `uniqueId`.

```
GET https://www.touchgal.ink/api/patch?uniqueId=a555829b
```

Real response (3,195 B; `introduction` truncated here, `tags` trimmed from 110 entries):

```json
{
  "id": 637,
  "uniqueId": "a555829b",
  "vndbId": "v19073",
  "vndbRelationId": null,
  "bangumiId": 172612,
  "steamId": null,
  "dlsiteCode": null,
  "name": "千恋＊万花",
  "introduction": "## 游戏介绍\r\n\r\n少女的恋心藏不住——\r\n\r\n在电车仍未开通的深山里、坐落着名为穂織（ほおり）的小镇。 …\n\n## PV鉴赏\r\n\r\n::kun-video{src=\"https://cloud.touchgaloss.com/2023/06/52c4e244dd20231121045637.mp4\"}\n\n## 支持正版\r\n\r\n::kun-link{href=\"http://www.yuzu-soft.com/products/senren/index.html\" text=\"点我前往购买, 游戏官网可能需要代理打开\"}",
  "banner": "https://cloud.touchgaloss.com/patch/637/banner/banner.avif",
  "status": 0,
  "view": 1857585,
  "download": 670935,
  "type": ["other","game","audio","image","fix","adult","save","uncensored","mod"],
  "language": ["zh-Hans","other","ja"],
  "platform": ["windows","emulator","apk","other","macos","linux","ipa"],
  "tags": ["PC","ADV","AVG","STEAM","萝莉","galgame","甜作","GAL","R18","纯爱","2016","废萌","桐谷華","…共有 110 项…","诅咒"],
  "alias": ["Senren * Banka","Senren＊Banka","천련 * 만화","千戀＊萬花","Senren * Banka: A Thousand Colors of Love","千変万化","千恋万花"],
  "contentLimit": "sfw",
  "ratingSummary": {
    "average": 8.4,
    "count": 517,
    "histogram": [{"score":1,"count":6},{"score":2,"count":2},{"score":3,"count":4},{"score":4,"count":4},{"score":5,"count":3},{"score":6,"count":18},{"score":7,"count":31},{"score":8,"count":219},{"score":9,"count":68},{"score":10,"count":162}],
    "recommend": {"strong_no":4,"no":12,"neutral":228,"yes":143,"strong_yes":130}
  },
  "user": {"id":1,"name":"Palentum","avatar":"https://cloud.touchgaloss.com/user/avatar/user_1/avatar-mini.avif"},
  "created": "Fri Jun 09 2023 03:03:32 GMT+0800 (China Standard Time)",
  "updated": "Sun Oct 25 2026 18:43:04 GMT+0800 (China Standard Time)",
  "_count": {"favorite_folder":5549,"resource":59,"comment":375},
  "isFavorite": false
}
```

Gotchas for a C# mapper:
- `created`/`updated` are **`Date.prototype.toString()` strings** (`Fri Jun 09 2023 03:03:32 GMT+0800 (China Standard Time)`), *not* ISO-8601 — `DateTime.TryParse` with `InvariantCulture` copes; do not use `JsonSerializer` DateTime handling blindly.
- `tags` is a flat array of **display names** (no ids, no grouping) — 110 entries for this game; the card in search/list has **no** tags at all.
- `vndbId` is `"v19073"` (with the `v`), `bangumiId` is numeric, `steamId` numeric or null, `dlsiteCode` string or null.
- `introduction` is **Markdown** and may embed custom directives `::kun-video{src="…"}` / `::kun-link{href="…" text="…"}` plus absolute `https://cloud.touchgaloss.com/uploads/*.avif` images.
- `contentLimit` is `"sfw"` or (presumably) `"nsfw"`; `status` is an int (0 = visible in normal mode).

### 4.3 Other verified GET endpoints

| Endpoint | Result |
|---|---|
| `GET /api/home` | 200, 11,564 B, `{"galgames":[ …same card shape… ]}` (measured in the no-DevTools channel) |
| `GET /api/home/random` | 200, 23 B: `{"uniqueId":"d878ba53"}` — cheapest "random game" probe |
| `GET /api/galgame?selectedType=all&selectedLanguage=all&selectedPlatform=all&sortField=created&sortOrder=desc&page=1&limit=2` | 200, 740 B, `{"galgames":[{…},{…}]}` — same schema as search but without `total` semantics of relevance |
| `GET /api/patch?uniqueId=bf409e51` | 200, 1,930 B — shorter intro for a newer entry (`vndbId":"v64175"`, `steamId":4622110`) |
| `GET /api/tag/all`, `/api/tag/galgame`, `/api/company/all`, `/api/company/galgame`, `/api/resource`, `/api/patch/introduction`, `/api/patch/comment`, `/api/user/mention/search` | routes exist; `/api/tag/all` without params answers 200 with a zod error string `"Invalid input: expected number, received NaN"` (it needs query params) |
| `GET /robots.txt` | 200, 397 B: `Disallow: /message /admin /user /tag`, `Disallow: /*?page=` |
| `GET /sitemap.xml` | 200, **1,668,269 B** `application/xml` — every page incl. all `/<uniqueId>` detail URLs, with `lastmod` (2026-10-24) |
| `GET /<uniqueId8>` e.g. `/bf409e51` | 200 HTML, 90,774 B (SSR detail page) |
| `GET /search` | 200 HTML, 52,997 B — **but the search box is client-side only** (`components/search/Container.tsx`, debounced React state); `?q=` in the URL does nothing, so you cannot drive search by navigation |

`POST /api/search/tag`, `/api/tag/search`, `/api/company/search` also exist (POST-only), irrelevant for a metadata plugin.

---

## 5. Why a plain-HTTP plugin cannot work (root cause, verified)

1. Cloudflare "managed challenge" (`cType: 'interactive'`, `cZone: 'www.touchgal.ink'`) is served to **every** path for clients whose TLS fingerprint is not a browser's, regardless of UA/headers/cookies.
2. `cf_clearance` is bound to the TLS/JA3+JA4 fingerprint → copying the cookie to `HttpClient`/`curl` always yields `403` again (measured: 403 on `/`, `/api/home`, `POST /api/search`).
3. Even a genuine browser is refused on **non-GET `/api/*`** once automation is detected (CDP/DevTools attached): 403 "Sorry, you have been blocked" (4,546 B) or a hung request.
4. There is no alternate hostname, no JSON feed (`/rss.xml`, `/feed*`, `/atom.xml` = 403 for everyone), no unprotected `/_next/data` route, and no public Meilisearch.

Because of (1)+(2), "paste cookie + UA into plugin settings" **cannot** work — do not build that.

---

## 6. Recommended plugin architecture (Playnite, .NET Framework 4.6.2)

### 6.1 Primary: embedded WebView2 (real Edge engine, no DevTools)

The verified fact that a *headless Edge with no DevTools* gets `200` on `POST /api/search` is exactly the WebView2 situation (WebView2 = same Edge engine + a host API, no CDP). Sketch:

```csharp
// one-time per session
var env   = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir); // persistent => cf_clearance survives restarts
var ctrl  = await env.CreateCoreWebView2ControllerAsync(hwnd);                   // hidden Form/Window, IsVisible = false
var core  = ctrl.CoreWebView2;
await core.NavigateAsync("https://www.touchgal.ink/");                           // challenge is solved transparently here
// wait for document ready (poll core.ExecuteScriptAsync("document.readyState"))

string js = @"
(async () => {
  const body = {
    queryString: JSON.stringify([{type:'keyword',mode:'include',name:" + JsonEscape(name) + @"}]),
    limit: 20, searchOption:{searchInIntroduction:false,searchInAlias:true,searchInTag:true},
    page: 1, selectedType:'all', selectedLanguage:'all', selectedPlatform:'all',
    sortField:'resource_update_time', sortOrder:'desc',
    selectedYears:['all'], selectedMonths:['all'], minRatingCount:0
  };
  const r = await fetch('/api/search', {
    method:'POST', credentials:'include',
    headers:{'Content-Type':'application/json','X-Requested-With':'kun-fetch'},
    body: JSON.stringify(body)
  });
  return JSON.stringify({ status: r.status, text: await r.text() });
})()";
string json = await core.ExecuteScriptAsync(js);   // returns a JSON-encoded string
```

Then `JsonConvert.DeserializeObject` twice (once for the envelope, once for `text`). Same call for
`fetch('/api/patch?uniqueId=' + id)` (GET) and `fetch('/api/home')`.

Practical notes:
- Playnite plugins run inside the Playnite process (x64, .NET Framework 4.6.2); WebView2 needs the Evergreen runtime — preinstalled on Windows 11 and on Windows 10 with Edge, and full Edge 155/156 is present here (`C:\Program Files (x86)\Microsoft\EdgeWebView\Application\155.0.4283.62`, runtime-only install, **no** managed `Microsoft.Web.WebView2.Core.dll` — the plugin must ship the NuGet SDK).
- A **persistent** `userDataFolder` keeps `cf_clearance`, so the challenge is usually only solved on the first run.
- Keep a hidden HostForm on the UI thread; Playnite plugins may create WPF/WinForms windows.
- Do **not** enable remote debugging (`--remote-debugging-port` / `CoreWebView2` DevTools) while testing, and do not attach a debugger to that process — that is precisely what flips the edge rule to 403.
- If a request ever returns 403 or hangs, re-navigate to `https://www.touchgal.ink/` and retry once (challenge re-solve).

### 6.2 Alternative engine: CefSharp (no runtime dependency)

Same idea, `Browser.ExecuteScriptAsync` + `JavascriptResponse.Result`. Heavier (~150 MB of CEF binaries), but self-contained and also "real Chromium, no CDP". Use `CefSettings.UserAgent` left at default.

### 6.3 External browser (fragile, not recommended)

`msedge.exe --headless=new --user-data-dir=… --load-extension=<probe ext>` worked, but it requires installing an unpacked extension into a private profile and scraping `INFO:CONSOLE` from stderr — unsuitable for shipping. Launching `msedge.exe --remote-debugging-port` does **not** work (403).

### 6.4 If the plugin must stay pure-HTTP: use NextMoe instead (§8), not TouchGal.

There is no TouchGal mirror to borrow: `www.moyu.moe` is a *different* Nuxt site ("鲲 Galgame 补丁站"), `api.*.touchgal.ink` does not exist, and no third-party host proxies the TouchGal JSON.

---

## 7. Image/asset handling

- TouchGal images are served from `https://cloud.touchgaloss.com/...` and are **not** challenge-protected:
  `GET /patch/637/banner/banner.avif` → 200 `application/octet-stream` (26,919 B for another entry);
  `GET /uploads/20261018041152151.avif` → 200 `image/avif` (151,675 B).
- Format is **AVIF** — WPF `BitmapImage`/`System.Drawing` on .NET Framework 4.6.2 cannot decode it natively, and Playnite's `MetadataFile` wants PNG/JPG. Options: convert (ImageSharp / libavif / magick), or take the cover from another source (NextMoe covers are WebP, also not natively decodable), or accept degraded (no cover) behaviour.
- `og:image` on the SSR page is the same AVIF banner.

---

## 8. Verified fallback source: NextMoe open API (plain HTTP, no Cloudflare)

`https://api.nextmoe.dev` answers `curl` directly (200, `application/json`), no challenge, no key for anonymous use. Same API that `ReinaManager` uses (`src/metadata/api/nextmoe.ts`). Documented at `https://developer.nextmoe.dev/`.

Search by name (name → candidates):

```
GET https://api.nextmoe.dev/v2/catalog/works?q=千恋万花&nsfw=true&limit=8
    &include=titles,companies
    &fields=medium,display_name,latin,localized,olang,content_rating,content_limit,cover,companies
→ 200 {"$schema":"https://api.nextmoe.dev/ListWork.json","items":[…],"next_cursor":…}
```

Details by id:

```
GET https://api.nextmoe.dev/v2/catalog/works/4
    ?include=titles,refs,intros,companies,ratings,tags,playtimes,releases
    &fields=medium,display_name,latin,localized,olang,content_rating,content_limit,cover,titles,refs,intros,companies,ratings,tags,playtimes,releases
    &nsfw=true&spoiler=none
→ 200, 44,415 B
```

Item shape (trimmed, real):

```json
{"id":"4","object":"work","medium":"galgame","display_name":"Summer Pockets","latin":null,
 "olang":"ja","content_rating":"all_ages","content_limit":"sfw",
 "localized":{"en":{"is_machine":false,"value":"Summer Pockets"},"ja":{"is_machine":false,"value":"Summer Pockets"},"zh-Hans":{"is_machine":false,"value":"夏日口袋"}},
 "cover":{"url":"https://image.kungal.iloveren.link/<2>/<2>/<sha256>.webp","height":900,"source":"vndb"},
 "titles":[18],"refs":[75],"intros":[3],"companies":[4],"ratings":[6],"tags":[89],"releases":[58]}
```

- `refs[].source` values seen: `bangumi`, `curated`, `erogamescape`, `howlongtobeat`, `vndb` (e.g. `{"external_id":"v20424","source":"vndb"}`), `dlsite`, `steam`, `dmm`, `getchu`. **No touchgal/moyu ref** → you cannot map a NextMoe work to a TouchGal `uniqueId`.
- `intros[]` = `[{"is_machine":false,"lang":"zh-Hans","source":"curated","value":"…"}, …]`
- `companies[]` = `{"display_name":"Key","attribution_role":"developer","company_kind":"publisher","id":"2","logo":{…}}`
- `ratings[]` = `{"source":…,"score":…,"rank":…,"vote_count":…,"stats":…,"distribution":[{"score":n,"count":m}]}` (compute the average from `distribution`; there is no flat average field)
- `tags[]` = `{"display_name":"Galgame","id":"4","source":"bangumi","is_sexual":false,"spoiler":"none","tag_kind":"meta","tier":"hidden","work_count":12833}`
- `releases[]` = `{"id":"158965","date":"2020-06-26","platform":"win","platforms":["win"],"release_kind":"digital","refs":[{"external_id":"VJ013550","source":"dlsite"}]}`
- Rate limits: anonymous ~800 ms between calls (ReinaManager throttles), `429` code `QUOTA_EXCEEDED`, `RateLimit`/`X-RateLimit-Reset` response headers.

Cross-reference for parity: TouchGal `千恋＊万花` = `vndbId v19073`, `bangumiId 172612`; NextMoe exposes the same `vndb`/`bangumi` refs, so a plugin could use NextMoe as the metadata source and merely build a TouchGal *link* when it happens to know the `uniqueId` (e.g. from the 1.67 MB sitemap or a prior TouchGal scrape).

---

## 9. Verified vs. inferred

**Verified by direct observation**
- 403 + challenge for every raw-HTTP request except `/cdn-cgi/trace`; `cf_clearance` paste does not help.
- The TouchGal source repo and its exact API contract (§4), including the 480 B search response and the 3,195 B detail response.
- `POST /api/search` returns 200 from a no-DevTools Edge (headless and headful) and 403 from a DevTools-attached Chrome (headless and headful).
- In that same no-DevTools channel: `/api/home` 200/11,564 B, `/api/home/random` 200/23 B, `/api/patch?uniqueId=bf409e51` 200/1,930 B, `/api/galgame…` 200/740 B, `GET /api/search` 405, `/api/tag/all` 200 with a zod error string.
- `robots.txt`, `sitemap.xml` (1,668,269 B), detail HTML, NextMoe endpoints, image CDNs, `moyu.moe` being a different app.

**Inferred (not directly observed — flag as risk)**
- That WebView2/CefSharp behave like the no-DevTools Edge used here. Same engine and no CDP, so very likely, but the WebView2 SDK could not be installed/tested here (runtime present, no managed DLL).
- That Cloudflare re-challenges a copied `cf_clearance` specifically because of TLS fingerprinting (the outcome is proven; the mechanism is Cloudflare's documented behaviour).
- `contentLimit: "nsfw"` value and `status` semantics (only `sfw`/`0` observed).

---

## 10. Reproduction commands

```powershell
# Always blocked (documented baseline)
curl.exe -s -o NUL -w "%{http_code}|%{content_type}|%{size_download}`n" `
  -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36" `
  --compressed https://www.touchgal.ink/api/home        # -> 403|text/html|5658

# Working channel (no DevTools): headless Edge + content-script probe, console scraping
msedge.exe --headless=new --user-data-dir=$env:TEMP\galtool\profEdgeS `
  --load-extension=$env:TEMP\galtool\ext2 --no-first-run --no-default-browser-check `
  --enable-logging=stderr --v=0 https://www.touchgal.ink/
# then: Select-String -Path <stderr> -Pattern TGRESULT

# GET-only debugging through CDP (POSTs via CDP are always 403):
powershell -File D:\文档\deepseek-harness\default-workspace\research\cdp.ps1 `
  -NavUrl "https://www.touchgal.ink/" -ExprFile "…\expr_samples.js" -Port 9341 -WaitMs 25000
```

Workspace artifacts: `research\cdp.ps1`, `research\expr_*.js`, `%TEMP%\galtool\json\DETAIL.json` (full 3,195 B detail sample), `%TEMP%\galtool\ext{,2,3}\` (extension probes).

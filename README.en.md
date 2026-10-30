<p align="center"><a href="./README.md">简体中文</a> | <b>English</b></p>

# Galgame metadata providers for Playnite (5 plugins)

Five Playnite **metadata provider** plugins that pull data from sites which currently have **no Playnite plugin at all**:

| Plugin | Site | Plugin ID | Folder | DLL |
| --- | --- | --- | --- | --- |
| Ymgal Metadata Provider | https://www.ymgal.games/ | `YmgalMetadata_2460c7c5-5f79-4ebe-826d-cba2084e2464` | same name | `YmgalMetadata.dll` |
| Kungal Metadata Provider | https://www.kungal.com/ | `KungalMetadata_664139ab-88b1-4be5-869e-592b81e21df6` | same name | `KungalMetadata.dll` |
| Hikarinagi Metadata Provider | https://www.hikarinagi.org/ | `HikarinagiMetadata_57db3d17-7ad1-4f98-83b3-bcffb08ef8d2` | same name | `HikarinagiMetadata.dll` |
| Shionlib Metadata Provider | https://shionlib.com/ | `ShionlibMetadata_e882fba8-217a-4fcd-a400-b53b3392a734` | same name | `ShionlibMetadata.dll` |
| TouchGAL Metadata Provider | https://www.touchgal.ink/ | `TouchgalMetadata_af145bc3-1883-4933-a1d7-d5124de2cccd` | same name | `TouchgalMetadata.dll` |

- Target framework: .NET Framework 4.6.2 (same as Playnite 10.51). No third-party DLLs; each plugin is standalone and can be enabled or disabled on its own.
- Current version **v0.6**. Tested on Playnite 10.51.0.23209: all five load together without errors, the four sources that need no token scrape fine, TouchGAL needs your own API token. The settings view and the manual selection window are dark-themed and available in **Chinese and English**.

## Installation

**Option 1: `.pext` package (recommended)**

1. Open the `releases\` folder and download the five `.pext` files (or only the ones you need).
2. In Playnite: main menu → **Add-ons** → **Install add-on** → pick the `.pext` file.
3. Restart Playnite; the five sources appear under **Settings → Metadata → Sources**.

**Option 2: copy by hand**

Copy the whole `dist\<PluginName>_<Guid>\` folder into `%APPDATA%\Playnite\Extensions\` and restart Playnite.

> Close Playnite completely before overwriting: the DLLs are locked while it runs.

**Option 3: one-click script (upgrading from a source checkout)**

Quit any running game, then double-click `tools\install-and-restart.cmd`. It stops Playnite, copies the newest five plugins from `dist\`, and starts Playnite again.

## Using it in Playnite

1. Select a game → right click → **Edit…** → **Metadata** tab → **Download metadata**.
2. In the metadata window tick the sources you want (Ymgal / Kungal / Hikarinagi / Shionlib / TouchGAL), choose the fields, and confirm.
3. Each source opens a **selection window**: pick the right entry on the left, click one of the site's background images on the right (the first one is preselected), then press **Use this entry**. Press **Skip this source** to ignore that source for this download.
4. You can also make them the default sources under **Settings → Metadata**.

The plugins hand these fields to Playnite: name, genres, release date, developers, publishers, tags, description, links, community score, cover, background, age rating, platforms and features.

## Manual match confirmation and background picker

This is the VNDB Nexus style workflow. It is on by default ("Confirm the match and pick a background image on every download"):

- **Window**: dark theme (`#1B1C1F` background, custom title bar, draggable and resizable), 1000×700, independent of the Playnite theme.

| Hikarinagi (banner + screenshots) | Kungal (banner + full CG set, 26 images) |
| --- | --- |
| ![Hikarinagi picker](docs/picker-dark-hikarinagi.png) | ![Kungal picker](docs/picker-dark-kungal.png) |


- **Left list**: every result the site returned for the game name (cover thumbnail, title, subtitle, developer/year, and the match score against your game name). The best match is preselected.
- **Right side**: after a candidate is selected the plugin loads its details and lays out **all background images** (banner plus every screenshot, usually 1920×1080). Click one to select it (thick blue border) and Playnite's "Background image" field uses that file; untick "Download background image" to skip backgrounds entirely and keep only text and cover.
  - How many images per site: Kungal has the most (`include=screenshots` returns the whole CG set, e.g. 25 for *Senren * Banka*), Hikarinagi and Shionlib return around 5, TouchGAL returns its banner, and Ymgal has no screenshots (empty background).
  - Note: in Playnite's **Download metadata** dialog you must tick the **Background image** field, otherwise Playnite never asks the plugin for it and your choice is not stored.
- **Skip this source**: nothing is written for that source and no error is raised.
- If the game's **Links** already contain the site page, the plugin matches by ID, puts that entry first and preselects it — but the window still opens so you can confirm (since v0.4; older builds skipped the window when a link matched).
- Playnite's **background metadata update** never opens the window; it always uses automatic matching.
- **Description line breaks**: newlines from the site are converted to HTML `<br>` before they reach Playnite (Playnite renders the description as HTML, plain newlines collapse into one paragraph). If you see `<br>` tags while editing the field, that is the stored form; it renders as a line break.

## Plugin settings (Settings → Extensions → select a plugin → Settings)

| Setting | Default | Meaning |
| --- | --- | --- |
| Language | 中文 | Language of the plugin UI: Chinese or English. The window is rebuilt immediately when you switch. |
| Title language | Use Chinese when available | Use the Chinese title when the site has one; "Use original title" always keeps the original |
| Description language | Use Chinese when available | Same, for the description |
| Include adult (R18) works in search results | On | Kungal **requires** this: R18 works are filtered out otherwise |
| Download cover image | On | The plugin downloads the image into memory first (avoids hotlink blocking) and falls back to handing Playnite a URL |
| Download background image | On | Sites with a banner/screenshots return 1920×1080 images; the picker lets you choose which one |
| Confirm the match and pick a background image on every download | On | Turn off to go back to fully automatic matching (no window) |
| Maximum number of tags (0 = unlimited) | 15 | 0 means unlimited |
| Accept the closest result when nothing matches exactly | Off | When off, a low score raises an error instead of writing a wrong match; when on, the first search hit is used |

## Matching rules and tips

- By default the **game name** is searched on the site, then a similarity score (character bigrams for Chinese/Japanese, plus containment) picks the best hit as the preselected candidate. If the score is not high enough, the plugin fetches the details of the top 3 candidates and compares every title (original, Chinese, aliases); it only uses one when they agree, otherwise it reports an error instead of writing garbage.
- With manual confirmation enabled, the automatic matcher only decides which candidate is preselected; you make the final call with **Use this entry**.
- **Recommended**: add the game's page on the site to the game's **Links** in Playnite. Later downloads then match by ID, put that entry first and preselect it, so it cannot drift to another entry (the window still opens for confirmation):

| Site | Link format to paste |
| --- | --- |
| Ymgal | `https://www.ymgal.games/ga22374` |
| Kungal | `https://www.kungal.com/galgame/15` |
| Hikarinagi | `https://www.hikarinagi.org/galgames/780` |
| Shionlib | `https://shionlib.com/zh/game/708` |
| TouchGAL | `https://www.touchgal.com/galgame/<8-char uniqueId>` |

- When a search fails or no confident match exists, Playnite shows an error notification in the lower right corner (background updates only write to the log).

## TouchGAL specifics

TouchGAL's website and web API sit behind a Cloudflare challenge, so plain HTTP requests get 403 (curl, cookie replay and headless browsers all fail). This plugin therefore uses the **official developer API**: `https://developer.touchgal.com/api/v1`.

You need your own API token:

1. Open <https://developer.touchgal.com>, sign up/log in and create a token.
2. In Playnite: **Settings → Extensions → TouchGAL Metadata Provider → Settings**, paste the token into **TouchGAL API token** and save.

Without a token that source explains what to do instead of failing silently; the other four sources need no account at all.

**TouchGAL has no screenshots and no PV**: its official API returns a single AVIF portrait cover per game (771×1080), which the plugin uses as the cover and leaves the background empty. The screenshots and the PV section on the website live behind a Cloudflare-protected endpoint that the plugin cannot reach. That cover is AVIF, so Windows needs the free **AV1 Video Extension** from the Microsoft Store to display it; without the extension the plugin skips the image instead of showing an empty frame.

## Field coverage per site

| Field | Ymgal | Kungal | Hikarinagi | Shionlib | TouchGAL |
| --- | --- | --- | --- | --- | --- |
| Chinese title | ✅ | ✅ | ✅ | ✅ | ✅ |
| Description (Chinese) | ✅ | ✅ | ✅ | ✅ | ✅ |
| Developers | ✅ | ✅ | ✅ | ✅ | ✅ |
| Publishers | ✅ | ✅ | ✅ | ❌ | ✅ |
| Tags | ✅ (staff credits, names only) | ✅ | ✅ | ✅ | ✅ |
| Score | ❌ | ✅ (aggregated) | ❌ (no aggregate) | ❌ | ✅ |
| Cover | ✅ | ✅ | ✅ | ✅ (R18 covers need "Include adult works") | ✅ |
| Background / screenshots | ❌ | ✅ banner + full CG set (~25) | ✅ screenshots | ✅ screenshots | ✅ banner |
| Age rating | ✅ (R18 flag) | ✅ | ✅ (inferred from covers/R18 tags) | partial | ✅ |
| Platforms | ✅ (releases) | ✅ | ✅ | ✅ | ✅ |
| Extra links | official site, Steam, … | VNDB/Bangumi/Steam/DLsite/EGS, … | official site/Steam/VNDB/Bangumi, … | VNDB/Bangumi/Hikarinagi + stores | official site |

Every site's tag list has noise removed (`Galgame`, `PC`, `ADV`, `R18`, `汉化`, bare years, company names, staff and voice actor names, …). Links are capped at 12 and deduplicated in the order "this site → VNDB → Bangumi → Steam → official site → stores".

## Known limitations

- **Ymgal**: tags are the names credited in the site's STAFF card (script, art, character design, music, songs) - names only, without the job prefix; the site itself has no work tags, no community score and no screenshots/backgrounds, only a main cover; the publisher comes from the site's publisher entries and is empty for some works.
- **Kungal**: search requires "Include adult (R18) works", otherwise the site filters R18 works out; tags occasionally contain a company's Chinese name or translation.
- **Hikarinagi**: no aggregate score, so the community score field stays empty; the age rating is inferred from cover flags and R18 tags and may be wrong for a few works.
- **Shionlib**: no publishers and no score; as a guest the R18 cover is blurred, so enable "Include adult works" to get the original.
- **TouchGAL**: requires your own API token; only a single banner is available as background; after reinstalling the plugin you have to confirm the token again.
- **Matching by title**: when the game name differs a lot from the site title (all-English names, romanisation, very long subtitles) matching can fail or pick the wrong entry. The reliable fix is to put the site page into the game's **Links**; the plugin then matches by ID (the window still opens for confirmation).
- **N sources = N windows**: selecting N sources opens N windows in sequence; confirm or skip each one.
- **Background updates never prompt**: Playnite's background metadata update uses automatic matching, which is less accurate than manual confirmation; for a whole library, scrape game by game.
- **Speed**: each site is throttled to 0.25–0.85 s per request, so one game usually takes 1–3 s; enabling image downloads makes it slower, and scraping a whole library takes a while.
- **APIs may change**: all five sites rely on unofficial or semi-public endpoints; a site redesign may require a plugin update.

## Contributing

Issues and pull requests are welcome. When a site's endpoint changes, the fix is concentrated in `src\<Site>\<Site>Source.cs`, and `tools\harness\Harness.exe` verifies the sources without starting Playnite.

## Acknowledgements

All metadata comes from the public APIs and public pages of the sites below. **Special thanks to these platforms for providing public APIs, data and resources** — without them this plugin would not exist.

| Site | Notes |
| --- | --- |
| [Ymgal 月幕 Galgame](https://www.ymgal.games/) | Chinese galgame database with an open `/open/archive` query API |
| [Kungal 鲲 Galgame](https://www.kungal.com/) | Galgame community and database |
| [Hikarinagi](https://www.hikarinagi.org/) | ACGN community |
| [Shionlib 书音的图书馆](https://shionlib.com/) | Galgame archive |
| [TouchGAL](https://www.touchgal.com/) | Galgame community with an official developer API |

The structured data behind the Kungal source actually comes from the [NextMoe Catalog API](https://api.nextmoe.dev/); thanks as well for that open endpoint. Cross references found in the site data belong to:

- [VNDB](https://vndb.org/) — The Visual Novel Database
- [Bangumi 番组计划](https://bangumi.tv/) — Bangumi
- [ErogameScape](https://erogamescape.dyndns.org/) — エロゲー批評空間
- [Steam](https://store.steampowered.com/) — the largest digital game store

The field layout, fetching strategy and implementation ideas were informed by these open source projects; thanks to their authors:

- [ReinaManager](https://github.com/huoshen80/ReinaManager)
- [LunaBox](https://github.com/Saramanda9988/LunaBox)
- [NekoBox](https://github.com/doudou0611/NekoBox)

If any of the sites or projects above consider this usage inappropriate, please open an issue and the relevant code will be adjusted or removed.

## Disclaimer

- This project only reads and reformats publicly available information. It stores and indexes nothing and circumvents no access control; every request is throttled (0.25–0.85 s per site).
- Data copyright belongs to the sites and rights holders; check the applicable terms yourself, and obtain permission from the site before commercial use.
- This project is not affiliated with Playnite or with any of the sites above.

## License

MIT, see [LICENSE](LICENSE).

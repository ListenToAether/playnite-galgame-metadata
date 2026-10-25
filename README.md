# Galgame 资料数据来源插件（Playnite Metadata Provider ×5）

为 Playnite 新增 5 个「资料数据来源」插件，分别从下列目前 **没有 Playnite 插件** 的站点抓取元数据：

| 插件 | 网站 | 插件 ID | 安装目录 | DLL |
| --- | --- | --- | --- | --- |
| Ymgal Metadata Provider | https://www.ymgal.games/ | `YmgalMetadata_2460c7c5-5f79-4ebe-826d-cba2084e2464` | 同名文件夹 | `YmgalMetadata.dll` |
| Kungal Metadata Provider | https://www.kungal.com/ | `KungalMetadata_664139ab-88b1-4be5-869e-592b81e21df6` | 同名文件夹 | `KungalMetadata.dll` |
| Hikarinagi Metadata Provider | https://www.hikarinagi.org/ | `HikarinagiMetadata_57db3d17-7ad1-4f98-83b3-bcffb08ef8d2` | 同名文件夹 | `HikarinagiMetadata.dll` |
| Shionlib Metadata Provider | https://shionlib.com/ | `ShionlibMetadata_e882fba8-217a-4fcd-a400-b53b3392a734` | 同名文件夹 | `ShionlibMetadata.dll` |
| TouchGAL Metadata Provider | https://www.touchgal.ink/ | `TouchgalMetadata_af145bc3-1883-4933-a1d7-d5124de2cccd` | 同名文件夹 | `TouchgalMetadata.dll` |

- 目标框架：.NET Framework 4.6.2（与 Playnite 10.51 一致），不依赖任何第三方 DLL，5 个插件各自独立、可单独启用/停用。
- 已在 Playnite 10.51.0.23209 上实测：5 个插件同时加载无报错，四个免 Token 的站点刮削正常，TouchGAL 需自备 API 令牌。设置界面与「手动选择」窗口均为中文、暗色主题。

## 安装

**方式一：用 .pext 安装包（推荐）**

1. 打开 `releases\` 目录，下载 5 个 `.pext` 文件（或者只需要其中几个）。
2. Playnite → 主菜单 → **附加组件** → **安装附加组件** → 选择 `.pext` 文件。
3. 重启 Playnite，在 **设置 → 元数据** 的「来源」列表里就能看到这 5 个来源。

**方式二：手动复制**

把 `dist\<插件名>_<Guid>\` 整个文件夹复制到 `%APPDATA%\Playnite\Extensions\`，重启 Playnite。

> 覆盖安装前请先完全退出 Playnite，否则 DLL 被占用会复制失败。

## 在 Playnite 里怎么用

1. 选中要刮削的游戏 → 右键 → **编辑…** → **元数据** 选项卡 → **下载元数据**。
2. 在元数据窗口的 **来源** 列表里勾选这 5 个来源（Ymgal / Kungal / Hikarinagi / Shionlib / TouchGAL），选择要下载的字段后确定。
3. 每个来源会弹出一个 **选择窗口**：左边选是哪一个条目，右边从该站点的全部背景图里点选一张（默认选中第一张），点「使用这个条目」确认；不想用这个来源就点「跳过这个来源」。
4. 也可以在 **设置 → 元数据** 里把它们设为默认使用的来源。

插件会把结果按字段交给 Playnite：名称、类型（Genres）、发售日、开发商、发行商、标签、简介、链接、社区评分、封面、背景、年龄分级、平台、特性。

## 手动选择匹配结果与背景图

这是仿 VNDB Nexus 的交互，默认开启（设置里的「每次刮削时手动确认匹配结果并选择背景图」）：

- **窗口外观**：暗色主题（`#1B1C1F` 底色、自绘标题栏、可拖动/可缩放），1000×700，不影响 Playnite 自身主题。

| Hikarinagi（banner + 截图） | Kungal（banner + 整组 CG，共 26 张） |
| --- | --- |
| ![Hikarinagi 选择窗口](docs/picker-dark-hikarinagi.png) | ![Kungal 选择窗口](docs/picker-dark-kungal.png) |


- **左侧候选列表**：显示该站点按游戏名搜到的全部结果（封面缩略图、标题、副标题、开发商/年份、与你游戏名的匹配度）。匹配度最高的那个会预先选中。
- **右侧背景图**：选中左栏条目后，插件会去取该条目的详情，并把 **全部背景图**（banner + 所有截图，通常 1920×1080）以缩略图铺开。点任意一张即选中（蓝色加粗边框），Playnite 的「背景图片」字段就会用这张；取消勾选「下载背景图」则不下载背景，只取文字和封面。
  - 各站背景图数量：Kungal 最多（`include=screenshots` 会带回整组 CG，例如《千恋＊万花》25 张）、Hikarinagi 与 Shionlib 为 5 张左右、TouchGAL 用 banner、Ymgal 没有截图（背景为空）。
  - 注意：Playnite 的「下载元数据」对话框里要 **勾选「背景图片」字段**，否则它根本不会向插件索取背景图，你选的这张也就不会写入。
- **跳过这个来源**：本次刮削该来源不提供任何数据（不会报错、不会乱填）。
- 如果你已经在游戏的「链接」里放了该站点的页面地址，插件会直接按 ID 精确获取，**不再弹窗**。
- 后台批量下载（Playnite 的「后台下载元数据」）不会弹窗，走的仍是自动匹配。

## 插件设置（设置 → 扩展 → 选择插件 → 设置）

| 设置 | 默认 | 说明 |
| --- | --- | --- |
| 标题语言 | 有中文时用中文 | 有中文标题时用中文，否则用原名；选「使用原标题」则始终用原名 |
| 简介语言 | 有中文时用中文 | 同上，用于简介 |
| 在搜索结果中包含成人（R18）作品 | 开 | Kungal 必须开启才能搜到 R18 作品（否则结果会被过滤） |
| 下载封面图片 | 开 | 插件会先把图片下载到内存再交给 Playnite（避免防盗链），失败则回退为给 Playnite 一个 URL |
| 下载背景图片 | 开 | 有 banner/截图的站点会返回 1920×1080 的图；手动选择窗口里可再挑具体哪一张 |
| 每次刮削时手动确认匹配结果并选择背景图 | 开 | 关闭后回到全自动匹配（不再弹窗） |
| 最大标签数量（0 = 不限制） | 15 | 0 = 不限 |
| 没有精确匹配时接受最相近的结果 | 关 | 关闭时，匹配度过低会报错而不是乱填；打开后直接取搜索第一条 |

设置界面全部为中文，Token 等长文本用输入框，标签数量用数字框，其余为开关。

## 匹配规则与技巧

- 默认用 **游戏名** 在该站搜索，先用相似度（中/日文按字符二元组、兼顾包含关系）挑选最佳结果作为预选项；如果分数不够高，会拿前 3 个候选的详情页核对全部标题（原名/中文名/别名），确认一致才使用，仍然对不上就报错，不会乱填。
- 打开「手动确认匹配结果」后，上面这套自动匹配只用来决定窗口里预先选中哪一条，最终由你点「使用这个条目」确认。
- **推荐**：先把该游戏的站点页面链接加到 Playnite 游戏的 **链接** 里，之后刮削会直接按 ID 精确获取，完全不会串号：

| 站点 | 可粘贴的链接格式 |
| --- | --- |
| Ymgal | `https://www.ymgal.games/ga22374` |
| Kungal | `https://www.kungal.com/galgame/15` |
| Hikarinagi | `https://www.hikarinagi.org/galgames/780` |
| Shionlib | `https://shionlib.com/zh/game/708` |
| TouchGAL | `https://www.touchgal.com/galgame/<8位 uniqueId>` |

- 搜索失败/无法确定匹配时，Playnite 右下角会弹出错误提示（后台批量更新时不弹，只写日志）。

## TouchGAL 的特别说明

TouchGAL 的网页/网页 API 受 Cloudflare 挑战保护，普通 HTTP 请求会被 403 拦截（实测 curl、带 cookie 的请求、headless 浏览器都不行），因此本插件改用其 **官方开发者 API**：`https://developer.touchgal.com/api/v1`。

需要你自己的 API Token：

1. 浏览器打开 <https://developer.touchgal.com>，注册/登录后创建 Token。
2. 在 Playnite：**设置 → 扩展 → TouchGAL Metadata Provider → 设置**，把 Token 粘贴到「TouchGAL API 令牌」输入框，保存。

没有 Token 时，该来源会提示这段说明而不会静默失败；其他 4 个来源完全不需要账号。

## 各站字段覆盖情况

| 字段 | Ymgal | Kungal | Hikarinagi | Shionlib | TouchGAL |
| --- | --- | --- | --- | --- | --- |
| 中文标题 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 简介（中文） | ✅ | ✅ | ✅ | ✅ | ✅ |
| 开发商 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 发行商 | ✅ | ✅ | ✅ | ❌ | ✅ |
| 标签 | ❌ | ✅ | ✅ | ✅ | ✅ |
| 评分 | ❌ | ✅（多站加权） | ❌（无聚合分） | ❌ | ✅ |
| 封面 | ✅ | ✅ | ✅ | ✅（R18 封面需开“包含成人作品”） | ✅ |
| 背景/截图 | ❌ | ✅ banner + 整组 CG（25 张左右） | ✅ 截图 | ✅ 截图 | ✅ banner |
| 年龄分级 | ✅（R18 标记） | ✅ | ✅（用封面/R18 标签推断） | 部分 | ✅ |
| 平台 | ✅（releases） | ✅ | ✅ | ✅ | ✅ |
| 额外链接 | 官网/Steam 等 | VNDB/Bangumi/Steam/DLsite/EGS 等 | 官网/Steam/VNDB/Bangumi 等 | VNDB/Bangumi/Hikarinagi + 官网商店 | 官网 |

所有站点的「标签」都会过滤掉无意义标签（`Galgame`、`PC`、`ADV`、`R18`、`汉化`、纯年份、厂商名、职员/声优名等），链接最多保留 12 条并按「本站 → VNDB → Bangumi → Steam → 官网 → 商店」排序去重。

## 已知限制

- Hikarinagi 没有聚合评分，Shionlib 也不提供评分，所以这两个来源不填「社区评分」。
- Ymgal 只有封面（`mainImg`），没有截图/banner，所以背景图为空。
- 少数 R18 作品在 Shionlib 没有成人标记，不会被标为 18+。
- 单个来源一个游戏约 1–3 秒（含图片下载）；首次搜索 Ymgal 要先取 OAuth token。
- 无 XAML：设置界面和选择窗口都是用 C# 代码构建的（本机没有 MSBuild/XAML 编译链），外观与原生设置页略有差异，功能一致；选择窗口自带暗色主题，不跟随 Playnite 的亮/暗主题切换。
- 手动确认开着时，一次刮削勾选几个来源就会依次弹几个窗口；嫌麻烦可以在设置里关掉，或先给游戏加上站点链接（有链接就直接按 ID 获取，不弹窗）。

## 重新构建与安装

```powershell
# 构建 5 个插件到 dist\
powershell -ExecutionPolicy Bypass -File .\tools\build.ps1

# 安装（复制到 Playnite 扩展目录，覆盖旧版本）
powershell -ExecutionPolicy Bypass -File .\tools\install.ps1

# 打包成 .pext（Playnite 的附加组件安装包）
powershell -ExecutionPolicy Bypass -File .\tools\pack.ps1
```

安装/覆盖后需要重启 Playnite 才会加载新版本（Playnite 只在启动时加载扩展）。

只在本机测试数据源（不需要 Playnite）：

```powershell
.\tools\harness\Harness.exe 千恋＊万花          # 5 个来源的抓取结果
.\tools\probe\Probe.exe 千恋＊万花              # 走 Playnite SDK 类型的字段级结果
.\tools\probe\Probe.exe 完全错误的标题 https://www.ymgal.games/ga22374 Ymgal   # 按链接精确匹配

# UI 冒烟测试（STA 线程里构造中文设置界面和手动选择窗口，不需要 Playnite）
powershell -ExecutionPolicy Bypass -File .\tools\build-ui.ps1
.\tools\ui\UiSmoke.exe
```

## 目录结构

```
(仓库根目录)
  src\Shared\MiniJson.cs        自带 JSON 解析（不依赖 Newtonsoft）
  src\Shared\Common.cs          HTTP/限速/文本相似度/图片下载/ScrapedGame 模型
  src\Shared\Sources.cs         ISiteSource 接口、自动匹配、平台映射、标签过滤
  src\Shared\PlayniteSupport.cs Playnite SDK 适配层（设置、中文设置界面、MetadataProvider）
  src\Shared\Picker.cs          手动选择窗口（候选条目 + 全部背景图点选）
  src\Ymgal\...                 每个站点：<Site>Source.cs（抓取）+ <Site>Plugin.cs（入口）
  tools\build.ps1               用 Roslyn csc + net462 参考程序集编译 5 个 DLL
  tools\build-harness.ps1       构建无 Playnite 依赖的抓取测试
  tools\build-probe.ps1         构建带 Playnite.SDK 的字段级测试
  tools\build-ui.ps1            构建 UI 冒烟测试（中文设置界面 + 选择窗口）
  dist\<Name>_<Guid>\           构建产物：extension.yaml + icon.png + DLL
```

## 故障排查

- 插件是否加载：`%APPDATA%\Playnite\playnite.log` 里搜 `ExtensionFactory:Loaded plugin`。
- 抓取日志：`%APPDATA%\Playnite\extensions.log`，logger 名为 `GalgameMetadata`（失败时会有 `Ymgal/Kungal/... download failed for "..."`）。
- 抓取结果不对：先在 `tools\harness\Harness.exe` 里用同一标题跑一遍确认站点侧数据，再检查游戏名是否与站点标题差异过大（可在游戏链接里加站点 URL 强制精确匹配）。

## 贡献

欢迎 Issue / PR。数据源接口若发生变化，改动集中在对应站点的 `src\<站点>\<站点>Source.cs`，`tools\harness\Harness.exe` 可以在不启动 Playnite 的情况下直接验证。

## 免责声明

- 本项目仅做公开信息的读取与格式化，不存储、不索引站点内容，也不绕过任何访问限制；抓取频率均做了限速（每站 0.25–0.85 秒一次）。
- 各站点数据的版权归原站点及权利人所有，请自行确认使用范围；商业用途请先取得站点授权。
- 与 Playnite 官方及上述任何站点均无隶属关系。

## 许可证

MIT，见 [LICENSE](LICENSE)。

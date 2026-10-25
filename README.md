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
- 当前状态：已安装到 `C:\Users\23\AppData\Roaming\Playnite\Extensions`，并在 Playnite 10.51.0.23209 中成功加载（`playnite.log` 18:58:42–18:58:43 记录 5 条 `Loaded plugin: ... version 1.0.0`，无报错）。

## 在 Playnite 里怎么用

1. 选中要刮削的游戏 → 右键 → **编辑…** → **元数据** 选项卡 → **下载元数据**。
2. 在元数据窗口的 **来源** 列表里勾选这 5 个来源（Ymgal / Kungal / Hikarinagi / Shionlib / TouchGAL），选择要下载的字段后确定。
3. 也可以在 **设置 → 元数据** 里把它们设为默认使用的来源。

插件会把结果按字段交给 Playnite：名称、类型（Genres）、发售日、开发商、发行商、标签、简介、链接、社区评分、封面、背景、年龄分级、平台、特性。

## 插件设置（设置 → 扩展 → 选择插件 → 设置）

| 设置 | 默认 | 说明 |
| --- | --- | --- |
| 标题语言 | 中文优先 | 有中文标题时用中文，否则用原名；选 `Original title` 则用原名 |
| 简介语言 | 中文优先 | 同上，用于简介 |
| 包含成人（R18）作品 | 开 | Kungal 必须开启才能搜到 R18 作品（否则结果会被过滤） |
| 下载封面图片 | 开 | 插件会先把图片下载到内存再交给 Playnite（避免防盗链），失败则回退为给 Playnite 一个 URL |
| 下载背景图片 | 开 | 有 banner/截图的站点会返回 1920×1080 的图 |
| 最大标签数 | 15 | 0 = 不限 |
| 无精确匹配时接受最相近结果 | 关 | 关闭时，匹配度过低会报错而不是乱填；打开后直接取搜索第一条 |

## 匹配规则与技巧

- 默认用 **游戏名** 在该站搜索，先用相似度（中/日文按字符二元组、兼顾包含关系）挑选最佳结果；如果分数不够高，会拿前 3 个候选的详情页核对全部标题（原名/中文名/别名），确认一致才使用，仍然对不上就报错，不会乱填。
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
2. 在 Playnite：**设置 → 扩展 → TouchGAL Metadata Provider → 设置**，把 Token 粘贴到「TouchGAL API token」输入框，保存。

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
| 背景/截图 | ❌ | ✅ banner | ✅ 截图 | ✅ 截图 | ✅ banner |
| 年龄分级 | ✅（R18 标记） | ✅ | ✅（用封面/R18 标签推断） | 部分 | ✅ |
| 平台 | ✅（releases） | ✅ | ✅ | ✅ | ✅ |
| 额外链接 | 官网/Steam 等 | VNDB/Bangumi/Steam/DLsite/EGS 等 | 官网/Steam/VNDB/Bangumi 等 | VNDB/Bangumi/Hikarinagi + 官网商店 | 官网 |

所有站点的「标签」都会过滤掉无意义标签（`Galgame`、`PC`、`ADV`、`R18`、`汉化`、纯年份、厂商名、职员/声优名等），链接最多保留 12 条并按「本站 → VNDB → Bangumi → Steam → 官网 → 商店」排序去重。

## 已知限制

- Hikarinagi 没有聚合评分，Shionlib 也不提供评分，所以这两个来源不填「社区评分」。
- Ymgal 只有封面（`mainImg`），没有截图/banner，所以背景图为空。
- 少数 R18 作品在 Shionlib 没有成人标记，不会被标为 18+。
- 单个来源一个游戏约 1–3 秒（含图片下载）；首次搜索 Ymgal 要先取 OAuth token。
- 无 XAML：设置界面是用 C# 代码构建的（本机没有 MSBuild/XAML 编译链），外观与原生设置页略有差异，功能一致。

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
```

## 目录结构

```
galgame-metadata\
  src\Shared\MiniJson.cs        自带 JSON 解析（不依赖 Newtonsoft）
  src\Shared\Common.cs          HTTP/限速/文本相似度/图片下载/ScrapedGame 模型
  src\Shared\Sources.cs         ISiteSource 接口、自动匹配、平台映射、标签过滤
  src\Shared\PlayniteSupport.cs Playnite SDK 适配层（设置、设置界面、MetadataProvider）
  src\Ymgal\...                 每个站点：<Site>Source.cs（抓取）+ <Site>Plugin.cs（入口）
  tools\build.ps1               用 Roslyn csc + net462 参考程序集编译 5 个 DLL
  tools\build-harness.ps1       构建无 Playnite 依赖的抓取测试
  tools\build-probe.ps1         构建带 Playnite.SDK 的字段级测试
  dist\<Name>_<Guid>\           构建产物：extension.yaml + icon.png + DLL
```

## 故障排查

- 插件是否加载：`C:\Users\23\AppData\Roaming\Playnite\playnite.log` 里搜 `ExtensionFactory:Loaded plugin`。
- 抓取日志：`C:\Users\23\AppData\Roaming\Playnite\extensions.log`，logger 名为 `GalgameMetadata`（失败时会有 `Ymgal/Kungal/... download failed for "..."`）。
- 抓取结果不对：先在 `tools\harness\Harness.exe` 里用同一标题跑一遍确认站点侧数据，再检查游戏名是否与站点标题差异过大（可在游戏链接里加站点 URL 强制精确匹配）。

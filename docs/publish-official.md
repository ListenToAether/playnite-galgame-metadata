# 发布到 Playnite 官方附加组件库

官方库仓库：<https://github.com/JosefNemec/PlayniteAddonDatabase>

## 一、当前状态：官方暂停接收新插件

仓库 README 原文（2026-10 核对）：

> Addon submissions are currently on hold. Primarily due to high increase of new submissions, people ignoring instructions, increasing concerns about plugin security and increased workload needed to verify new submissions. There's new addon database planned for Playnite 11 release, with new submission and verification system, which will be backported to Playnite 10 as well (planned to be done by the end of 2026). At which point we will resume plugin submissions via new addon portal.
>
> We might accept new addons from long standing trusted members of the community, if you are one of those and you want to submit new addon, please contact Crow on Playnite's Discord. Same if you want to make changes to your existing manifests.

结论：

- 现在提 PR 大概率不会被合并，新系统预计 **2026 年底前** 上线（同时回移到 Playnite 10）。
- 社区里长期活跃的成员可以找 Playnite Discord 上的 **Crow** 特批。
- 在进入官方库之前，插件**不会**出现在内置「附加组件」浏览器里，也**没有自动更新**；用 `.pext` 或复制目录的方式手动安装照常可用。

## 二、已准备好的材料

| 文件 | 用途 |
| --- | --- |
| `addons/metadata/galgame-metadata_*.yaml` | 提交给官方库的 5 个 addon 清单 |
| `manifests/*.yaml` | 5 个 installer manifest，被上面清单的 `InstallerManifestUrl` 引用 |
| `releases/*.pext` | 5 个安装包 |

## 三、提交前必须先做的事

1. **建一个 tag 为 `v0.9` 的 Release**，把 `releases/` 下的 5 个 `.pext` 作为附件上传，文件名保持不变。
   installer manifest 里写死的下载地址是：

   ```
   https://github.com/ListenToAether/playnite-galgame-metadata/releases/download/v0.9/<AddonId>.pext
   ```

   目前仓库里的 Release 是「整仓库源码 zip」，路径对不上，需要替换。

2. **确认 `main` 分支上存在 `manifests/` 目录**，否则 `InstallerManifestUrl` 会 404。

## 四、提交步骤（等通道重开后）

1. Fork <https://github.com/JosefNemec/PlayniteAddonDatabase>
2. 在 `addons/metadata/` 下新建 5 个 `.yaml`，内容直接取自本仓库 `addons/metadata/`
3. 提 PR；说明里写清楚插件用途、数据来源，并说明所有接口都是公开 API
4. 合并后即可使用：
   - 一键安装：`playnite://playnite/installaddon/<AddonId>`
   - 网页查看：`https://playnite.link/addons.html#<AddonId>`

## 五、AddonId 对照表

| 插件 | AddonId |
| --- | --- |
| Ymgal Metadata Provider | `YmgalMetadata_2460c7c5-5f79-4ebe-826d-cba2084e2464` |
| Kungal Metadata Provider | `KungalMetadata_664139ab-88b1-4be5-869e-592b81e21df6` |
| Hikarinagi Metadata Provider | `HikarinagiMetadata_57db3d17-7ad1-4f98-83b3-bcffb08ef8d2` |
| Shionlib Metadata Provider | `ShionlibMetadata_e882fba8-217a-4fcd-a400-b53b3392a734` |
| TouchGAL Metadata Provider | `TouchgalMetadata_af145bc3-1883-4933-a1d7-d5124de2cccd` |

这 5 个 ID 与各自 `extension.yaml` 里的 `Id` 完全一致，官方库要求两者必须相同。

## 六、字段说明（官方规范要点）

addon 清单（提交到官方库）：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `AddonId` | ✓ | 必须与插件 `extension.yaml` 的 `Id` 一致 |
| `Type` | ✓ | 资料数据来源填 `MetadataProvider` |
| `Name` / `Author` / `ShortDescription` | ✓ | 名称、作者、一句话简介 |
| `InstallerManifestUrl` | ✓ | 指向 installer manifest 的 URL |
| `SourceUrl` | ✓ | 源码仓库地址 |
| `Description` / `Tags` / `Links` / `Screenshots` / `IconUrl` |  | 选填 |

installer manifest（放在自己仓库）：

| 字段 | 说明 |
| --- | --- |
| `AddonId` | 同上 |
| `Packages[].Version` | .NET 版本字符串，如 `1.0.0` |
| `Packages[].PackageUrl` | `.pext` 的直接下载地址 |
| `Packages[].RequiredApiVersion` | 插件编译时用的 SDK 版本，本项目为 `6.15.0` |
| `Packages[].ReleaseDate` | `YYYY-MM-DD` |
| `Packages[].Changelog` | 变更列表 |

## 七、校验

Playnite 官方 **Toolbox** 可以离线校验清单格式与字段完整性：
<https://playnite.link/docs/master/tutorials/toolbox.html>

已安装官方库的插件，Playnite 会读取 `InstallerManifestUrl` 做版本比对，据此实现自动更新。

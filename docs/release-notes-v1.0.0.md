首个公开版本，包含 5 个 Playnite 元数据来源插件。适用于已经安装 Playnite 10.51+ 的 Windows，插件本身依赖 .NET Framework 4.6.2（Playnite 自带）。

## 包含的插件

| 来源 | 站点 | 插件 ID |
| --- | --- | --- |
| Ymgal | https://www.ymgal.games/ | `YmgalMetadata_2460c7c5-5f79-4ebe-826d-cba2084e2464` |
| Kungal | https://www.kungal.com/ | `KungalMetadata_664139ab-88b1-4be5-869e-592b81e21df6` |
| Hikarinagi | https://www.hikarinagi.org/ | `HikarinagiMetadata_57db3d17-7ad1-4f98-83b3-bcffb08ef8d2` |
| Shionlib | https://shionlib.com/ | `ShionlibMetadata_e882fba8-217a-4fcd-a400-b53b3392a734` |
| TouchGAL | https://www.touchgal.ink/ | `TouchgalMetadata_af145bc3-1883-4933-a1d7-d5124de2cccd` |

## 功能

- 中/日文标题模糊匹配；也可以把站点页面地址加到游戏的「链接」里，直接按 ID 精确获取、不再弹窗
- 仿 VNDB Nexus 的手动选择窗口（暗色主题）：左侧选条目，右侧点选该站点的**全部背景图 / CG**
- 字段：名称、类型、发售日、开发商、发行商、标签、简介、链接、社区评分、封面、背景、年龄分级、平台、特性
- 设置界面为中文，可切换中文/原标题与简介、开关 R18 结果、限制标签数量
- 每个来源独立成包，可单独启用/停用；后台批量刮削时走自动匹配、不弹窗

## 安装

1. 下载下面的 `.pext` 文件（按需选择，不必全装，但建议全装）
2. 打开 Playnite → 主菜单 → **附加组件** → **安装附加组件**
3. 选择下载的 `.pext` → 重启 Playnite
4. 在 **设置 → 元数据** 的「来源」列表里勾选这 5 个来源

## 注意

- **TouchGAL 需要自备开发者 API Token**：到 https://developer.touchgal.com 创建后，粘贴到「设置 → 扩展 → TouchGAL Metadata Provider → 设置」；其余 4 个站点无需登录
- 刮削时如果 Playnite 的「下载元数据」对话框里没有勾选「背景图片」，插件不会索取背景图，你在选择窗口里挑的那张也不会写入
- 数据版权归各站点所有，本项目只做公开信息的读取与格式化，请勿用于批量抓取或再分发

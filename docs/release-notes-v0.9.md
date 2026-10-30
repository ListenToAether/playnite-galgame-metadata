## v0.9

### 修复

- 无法解码的图片（例如 TouchGAL 的 AVIF 封面）不再在 Playnite 里显示成空白框，而是直接跳过，并在 `playnite.log` 里记录一行警告。
- TouchGAL 的竖版封面（771×1080）不再同时被当作背景图，避免背景被拉伸变形。
- 手动选择窗口遇到无法预览的图片时，会在底部提示这是 AVIF/WebP 格式、当前 Windows 缺少解码器。

### 说明

- TouchGAL 官方接口对每个游戏只返回一张 AVIF 竖版封面，没有截图，也没有 PV；网站上的「游戏截图」「PV鉴赏」位于 Cloudflare 保护的接口之后，插件无法获取。
- 若希望在 Playnite 中显示 TouchGAL 的封面，请安装 Microsoft Store 的「AV1 视频扩展」（免费），系统获得 AVIF 像素解码能力后即可正常显示。

### 安装

下载对应的 `.pext` 并在 Playnite 里导入，或把 `dist` 下的文件夹复制到 `%APPDATA%\Playnite\Extensions`。覆盖前请先退出 Playnite。

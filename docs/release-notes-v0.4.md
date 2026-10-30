为 Playnite 抓取 Ymgal、Kungal、Hikarinagi、Shionlib、TouchGAL 五个站点的元数据。本次 v0.4 集中修复手动选择窗口与描述渲染的问题。

### 修复

- **描述不再挤成一整段**：站点简介里的换行现在转成 HTML `<br>` 再交给 Playnite。Playnite 的描述框按 HTML 渲染，之前直接给纯文本换行会被折叠掉。
- **顶部白条消失**：无边框可缩放窗口上，Windows 会额外画一条浅色 DWM 边框。改用 `WindowChrome` 后由自绘的暗色标题栏完全接管。
- **候选标题不再溢出**：左侧列表里的超长标题改为省略号截断，不再压到右侧背景图区域。

### 改进

- **每次刮削都会弹窗确认**：以前游戏「链接」里已有站点地址时会直接按 ID 取值、跳过窗口；现在改成精确命中后仍弹出窗口，只是把该条目排在候选首位并预先选中，方便你核对后再确认。
- 简介清洗统一走 `CleanBlock`：自动识别站点返回的本来就是 HTML 的情况，避免二次转义出现 `&lt;br&gt;`。

### 安装

1. 下载下方 5 个 `.pext`（只要用到的站点即可）。
2. Playnite → 主菜单 → **附加组件** → **安装附加组件** → 选择 `.pext`。
3. 重启 Playnite，在 **设置 → 元数据** 的「来源」里勾选要用的来源。

已经是旧版的直接覆盖安装即可，配置会保留（TouchGAL 的 API 令牌需要重新确认一次）。

> TouchGAL 需要自备 API 令牌：在插件设置里填入 [developer.touchgal.com](https://developer.touchgal.com) 创建的 token，其余四个站点开箱即用。

完整说明见仓库内的 [README](https://github.com/ListenToAether/playnite-galgame-metadata/blob/main/README.md)。

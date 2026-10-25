# 把插件上传到 GitHub（网页操作版）

上传包：`dist\repo\playnite-galgame-metadata.zip`（39 个文件，约 1.8 MB）

---

## 一、解压

把 zip 解压到桌面，会得到一个文件夹 `playnite-galgame-metadata`，里面是：

```
src\        源码（5 个插件 + 共享代码）
tools\      构建/安装/测试脚本
docs\       站点 API 调研文档 + 选择窗口截图
releases\   5 个 .pext 安装包
README.md   仓库首页说明
LICENSE     MIT
.gitignore
```

## 二、建仓库

1. 打开 <https://github.com/new>
2. **Repository name** 填：`playnite-galgame-metadata`
3. 选 **Public**
4. **不要**勾 "Add a README file"（我们已经有了）、**不要**选 .gitignore、**不要**选 license
5. 点 **Create repository**

## 三、上传文件

1. 在新仓库页点 **uploading an existing file**（或 `Add file` → `Upload files`）
2. 打开解压出来的 `playnite-galgame-metadata` 文件夹，**全选里面的内容**（Ctrl+A：`src`、`tools`、`docs`、`releases`、`README.md`、`LICENSE`、`.gitignore`）
   - 注意：拖的是文件夹**里面**的内容，不要拖外层文件夹本身，否则仓库里会多一层目录
3. 拖进浏览器页面，等全部文件列出来
4. 下方 **Commit message** 填：
   ```
   Initial commit: 5 Playnite metadata providers (Ymgal/Kungal/Hikarinagi/Shionlib/TouchGAL)
   ```
5. 点 **Commit changes**

> 网页上传限制：单文件 ≤ 25 MB、一次 ≤ 100 个文件，我们的包完全够。

## 四、填写仓库信息

仓库首页右侧 **About** → 齿轮图标：

**Description**

```
Playnite 元数据来源插件：Ymgal / Kungal / Hikarinagi / Shionlib / TouchGAL，支持手动选条目与背景图点选
```

**Topics**（逐个输入后回车）

```
playnite  playnite-extension  playnite-plugin  metadata-provider  galgame  visual-novel  ymgal  kungal  hikarinagi  shionlib  touchgal  csharp
```

## 五、发布 v0.4 Release（推荐）

Releases 里的 .pext 让别人能直接下载安装，也方便你填 TouchGAL 的申请表单。

1. 仓库右侧 **Releases** → **Create a new release**
2. **Choose a tag** 填 `v0.4` → 点 `Create new tag: v0.4 on publish`
3. **Release title** 填：`v0.4 — 完善手动选择、描述渲染与窗口样式`
4. **Describe this release** 粘贴：

```markdown
首个公开版本，包含 5 个 Playnite 元数据来源插件。

## 包含的插件

| 来源 | 站点 | 插件 ID |
| --- | --- | --- |
| Ymgal | https://www.ymgal.games/ | `YmgalMetadata_2460c7c5-5f79-4ebe-826d-cba2084e2464` |
| Kungal | https://www.kungal.com/ | `KungalMetadata_664139ab-88b1-4be5-869e-592b81e21df6` |
| Hikarinagi | https://www.hikarinagi.org/ | `HikarinagiMetadata_57db3d17-7ad1-4f98-83b3-bcffb08ef8d2` |
| Shionlib | https://shionlib.com/ | `ShionlibMetadata_e882fba8-217a-4fcd-a400-b53b3392a734` |
| TouchGAL | https://www.touchgal.ink/ | `TouchgalMetadata_af145bc3-1883-4933-a1d7-d5124de2cccd` |

## 功能

- 中/日文标题模糊匹配，也可用游戏「链接」里的站点地址精确锁定条目
- 仿 VNDB Nexus 的手动选择窗口（暗色主题）：左侧选条目，右侧点选该站全部背景图/CG
- 字段：名称、类型、发售日、开发商、发行商、标签、简介、链接、社区评分、封面、背景、年龄分级、平台、特性
- 设置界面中文，可切换中/原文标题与简介、开关 R18 结果、限制标签数量
- 每个来源独立成包，可单独启用/停用

## 安装

下载下面的 `.pext` → Playnite 主菜单 → 附加组件 → 安装附加组件 → 选择文件 → 重启 Playnite。

## 注意

- TouchGAL 需要自备开发者 API Token（https://developer.touchgal.com），其余 4 个站点免登录
- 需要 Playnite 10.51 / .NET Framework 4.6.2 及以上
```

5. **Attach binaries**：把 `releases` 文件夹里的 5 个 `.pext` 拖进去
6. 点 **Publish release**

## 六、更新已有仓库（改了代码之后）

仓库已经建好、文件也在上面了，之后每次改完只需「覆盖上传」：

1. 本地重新构建并打包（在 `galgame-metadata` 目录下）：
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\build.ps1
   powershell -ExecutionPolicy Bypass -File .\tools\pack.ps1
   powershell -ExecutionPolicy Bypass -File .\tools\make-repo.ps1
   ```
   最后一条会重新生成 `dist\repo\playnite-galgame-metadata.zip`；把它解压到桌面。
2. 打开仓库页面 → `Add file` → **Upload files**。
3. 打开解压出的 `playnite-galgame-metadata` 文件夹，`Ctrl+A` 全选里面的内容，拖进网页。
   - GitHub 会按**文件路径**比对：内容变了的显示为修改，新文件显示为新增，内容没变的不产生改动，所以整包拖上去也只会提交真正变化的部分。
4. **Commit message** 写清楚改了什么，例如：
   ```
   Dark picker theme + full Kungal CG list
   ```
5. 点 **Commit changes**（若已有同名分支，选 "Commit directly to the main branch"）。

几个网页上传做不到的地方：

- **删除文件**不会同步：本地删掉的文件，要在仓库里点进那个文件 → 右上垃圾桶 → Commit 才会消失。
- **重命名**等于「删旧 + 加新」，旧文件同样要手动删。
- 提交历史是一条条网页 commit，没有本地分支/回滚能力。

如果想要正常的 `git push` 工作流（推荐长期使用），装一个 Git 即可：

- 图形化最省事：**GitHub Desktop** <https://desktop.github.com/>（安装后登录账号，Clone 仓库到本地，之后就是「提交 → Push」两个按钮）。
- 命令行：**Git for Windows** <https://git-scm.com/download/win>，装完后在仓库目录：

  ```bash
  git clone https://github.com/<用户名>/playnite-galgame-metadata.git
  # 把本地文件复制进去（或直接在 clone 出来的目录里改代码）
  git add -A
  git commit -m "说明这次改了什么"
  git push
  ```

  首次 push 会要求登录：浏览器授权，或用 Personal Access Token 当密码。

## 七、之后

- 仓库地址形如 `https://github.com/<你的用户名>/playnite-galgame-metadata`，TouchGAL 申请表单里的「项目地址」就填它
- 以后改代码：可以在 GitHub 网页直接编辑单个文件，或者装 GitHub Desktop / Git 后 `git clone` 本地提交

---

## 附：本机没有 git 时，如何用命令行上传（可选）

如果以后装了 Git：

```bash
cd 解压目录\playnite-galgame-metadata
git init
git add .
git commit -m "Initial commit"
git branch -M main
git remote add origin https://github.com/<用户名>/playnite-galgame-metadata.git
git push -u origin main
```

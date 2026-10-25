# playnite-galgame-metadata
Playnite 资料数据来源插件（开源，MIT）。为 Playnite 补充 5 个 Galgame 站点的元数据来源，其中 TouchGAL 通过官方开发者 API 读取数据。 调用范围仅两个只读接口：GET /api/v1/games/search 与 GET /api/v1/games/{uniqueId}（均带 allowNsfw 参数），无写入、无批量爬取。 使用场景：用户在 Playnite 里手动/批量刮削自己的本地游戏库，单用户低频（每次刮削 1–2 次请求，请求间隔 ≥600ms），数据仅写入用户本机数据库，不缓存、不再分发、不对外提供查询服务。 插件不含任何 TouchGAL 内容镜像，只把官方 API 返回的字段展示给用户

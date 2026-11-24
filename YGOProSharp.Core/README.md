# YGOProSharp.Core

`YGOProSharp.Core` 保存可复用的领域能力，不承载网络协议、服务端房间状态或 native duel 生命周期。

## 职责

- 卡片领域模型：`Card`、`NamedCard`。
- 卡片仓库与分层叠加：`ICardRepository`、`INamedCardRepository`、`InMemoryCardRepository`、`CompositeCardRepository`、`CompositeNamedCardRepository`、`ReloadableCardRepository`（带 `CatalogVersion` 原子热重载）。
- SQLite 数据库加载与结构预检：`SqliteCardDatabaseManager`（支持单库/多 `.cdb` 覆盖加载与 `ValidateDatabase` 工作副本检查）。
- native card callback adapter：`RepositoryCardDataProvider`。
- 卡组、`.ydk` 与随机组卡引擎：`Deck`（支持 OCG / TCG / DIY Custom `ot=4` 校验）、`DeckRules`、`YdkSerializer`、`PresetDeckPool`、`RandomDeckGenerator`（支持盲盒预组、1v1/2v2 对称镜像、平衡配比混沌组卡与 DIY 专属随机）。
- 禁限表：`Banlist`、`BanlistManager`（支持多 `lflist.conf` 合并/前置加载、`$whitelist`、按名称或 Hash 查询）。
- 分层脚本读取与热重载：`FileScriptProvider`、`ZipArchiveScriptProvider`（支持内存拷贝免文件锁读取 `.ypk` / `.zip`）、`InMemoryScriptProvider`、`CompositeScriptProvider`、`ReloadableScriptProvider`。
- 受管 DIY 包管理：`DiyPackageManager`（`manifest.json` 旁路元数据、路径穿越与 ZIP Slip 拦截、Lua 危险调用静态审计、卡密覆盖冲突预分析、`.trash` 回收站恢复）。
- OCG message 基础读取能力。

## 边界

Core 不引用 Protocol、Server、NativeApi 或 WindBot，也不引入第三方解压库。SQLite 类型只应集中在数据库 manager 实现中，不能泄漏到 Card、Deck、Game 或 native provider 之外。

## 常用命令

```bash
dotnet build YGOProSharp.Core/YGOProSharp.Core.csproj -c Release
```

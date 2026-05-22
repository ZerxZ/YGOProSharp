# YGOProSharp.Abstractions

`YGOProSharp.Abstractions` 保存跨项目共享的最小契约。

## 职责

- 定义 OCG runtime、duel session、card/script provider 等接口。
- 定义 native DTO，例如 `OcgCardData`。
- 提供全局日志入口 `AppLog`。
- 保存跨层常量和枚举引用所需的公共类型。

## 边界

本项目不引用 Core、Protocol、Server、NativeApi、SQLite 或第三方服务端实现。它应该保持轻量，避免把具体运行策略泄漏给下游项目。

## 使用者

Core、Protocol、NativeApi、Server、Cli、WindBot 和 Tests 都可以引用本项目。

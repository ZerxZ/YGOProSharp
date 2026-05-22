# YGOProSharp.Tests

`YGOProSharp.Tests` 是当前 solution 的轻量测试项目。

## 职责

- 验证核心行为，例如卡组、卡库 provider、NativeApi wrapper 边界。
- 验证 Protocol / Network 的基础行为。
- 验证 Server / Player / Game 的关键路径日志和状态流。
- 验证架构边界，防止旧依赖或 Console 输出回流。

## 运行

```bash
dotnet run --project YGOProSharp.Tests/YGOProSharp.Tests.csproj -c Release
```

如果已经构建过：

```bash
dotnet run --project YGOProSharp.Tests/YGOProSharp.Tests.csproj -c Release --no-build
```

## 边界

测试可以引用多个项目，但测试代码不应把旧 `YGOSharp`、`MDPro3`、直接 `Console.WriteLine` 或不符合当前分层的依赖重新带回生产代码。

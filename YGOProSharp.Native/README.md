# YGOProSharp.Native

[English](#english) | [中文](#中文)

---

## English

### Overview

`YGOProSharp.Native` is the native runtime package for YGOProSharp. It provides prebuilt `ocgcore` binaries only.

This project does **not** expose a managed API. Managed wrappers live in `YGOProSharp.NativeApi`, and application code should use the contracts from `YGOProSharp.Abstractions`.

Native sources:

- `ygopro-core`: <https://github.com/Fluorohydride/ygopro-core>
- Lua: <https://www.lua.org/>

### Runtime Assets

The package follows the standard .NET native asset layout:

```text
runtimes/<rid>/native/
```

| RID | Native file |
|---|---|
| `win-x64` | `ocgcore.dll` |
| `win-arm64` | `ocgcore.dll` |
| `linux-x64` | `libocgcore.so` |
| `linux-arm64` | `libocgcore.so` |
| `osx-x64` | `libocgcore.dylib` |
| `osx-arm64` | `libocgcore.dylib` |

### Local Build

Install xmake first:

```bash
# Windows PowerShell
iwr https://xmake.io/psget.text -UseBasicParsing | iex

# macOS / Linux
curl -fsSL https://xmake.io/shget.text | bash
```

Build for the current platform:

```bash
cd YGOProSharp.Native
xmake f -c -m release -y
xmake build ocgcore -y
```

Build for a specific platform and architecture:

```bash
# Windows x64
xmake f -c -p windows -a x64 -m release -y
xmake build ocgcore -y

# Windows ARM64
xmake f -c -p windows -a arm64 -m release -y
xmake build ocgcore -y

# Linux x64
xmake f -c -p linux -a x64 -m release -y
xmake build ocgcore -y

# Linux ARM64
xmake f -c -p linux -a arm64 -m release -y
xmake build ocgcore -y

# macOS x64
xmake f -c -p macosx -a x64 -m release -y
xmake build ocgcore -y

# macOS ARM64
xmake f -c -p macosx -a arm64 -m release -y
xmake build ocgcore -y
```

Build outputs are copied to:

```text
YGOProSharp.Native/lib/<rid>/
```

### CI Artifacts

GitHub Actions builds the native core for each supported RID. Each job uploads the corresponding `lib/<rid>/` directory as an artifact, and the packaging job downloads those artifacts before running `dotnet pack`.

### Packaging

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Release -o artifacts/nuget
```

The generated package includes native binaries under `runtimes/<rid>/native/`.

### Troubleshooting

`EntryPointNotFoundException` usually means the current `ocgcore` binary does not export a function required by the managed layer. Compare `ygopro-core/ocgapi.h` with the export table of the built binary.

If the application cannot find the native library at runtime, confirm that the output directory or NuGet package contains the `ocgcore` file for the current RID.

---

## 中文

### 概览

`YGOProSharp.Native` 是 YGOProSharp 的原生 runtime 包，只提供预构建的 `ocgcore` 二进制文件。

这个项目 **不提供托管 API**。托管封装位于 `YGOProSharp.NativeApi`，业务代码应依赖 `YGOProSharp.Abstractions` 中的接口。

原生来源：

- `ygopro-core`: <https://github.com/Fluorohydride/ygopro-core>
- Lua: <https://www.lua.org/>

### Runtime 资源

NuGet 包遵循标准 .NET native runtime asset 布局：

```text
runtimes/<rid>/native/
```

| RID | 原生文件 |
|---|---|
| `win-x64` | `ocgcore.dll` |
| `win-arm64` | `ocgcore.dll` |
| `linux-x64` | `libocgcore.so` |
| `linux-arm64` | `libocgcore.so` |
| `osx-x64` | `libocgcore.dylib` |
| `osx-arm64` | `libocgcore.dylib` |

### 本地构建

先安装 xmake：

```bash
# Windows PowerShell
iwr https://xmake.io/psget.text -UseBasicParsing | iex

# macOS / Linux
curl -fsSL https://xmake.io/shget.text | bash
```

构建当前平台：

```bash
cd YGOProSharp.Native
xmake f -c -m release -y
xmake build ocgcore -y
```

构建指定平台和架构：

```bash
# Windows x64
xmake f -c -p windows -a x64 -m release -y
xmake build ocgcore -y

# Windows ARM64
xmake f -c -p windows -a arm64 -m release -y
xmake build ocgcore -y

# Linux x64
xmake f -c -p linux -a x64 -m release -y
xmake build ocgcore -y

# Linux ARM64
xmake f -c -p linux -a arm64 -m release -y
xmake build ocgcore -y

# macOS x64
xmake f -c -p macosx -a x64 -m release -y
xmake build ocgcore -y

# macOS ARM64
xmake f -c -p macosx -a arm64 -m release -y
xmake build ocgcore -y
```

构建完成后，原生文件会复制到：

```text
YGOProSharp.Native/lib/<rid>/
```

### CI 构建产物

GitHub Actions 会按 RID 构建 `ocgcore`。每个 native job 上传对应的 `lib/<rid>/` 目录作为 artifact，最终 packaging job 下载这些 artifact 后执行 `dotnet pack`。

### 打包

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Release -o artifacts/nuget
```

生成的 NuGet 包会把原生二进制放到 `runtimes/<rid>/native/`。

### 常见问题

`EntryPointNotFoundException` 通常表示当前 `ocgcore` 没有导出托管层需要的函数。请对照 `ygopro-core/ocgapi.h` 和构建产物的 export table。

运行时找不到 native library 时，请确认应用输出目录或 NuGet 包中存在当前 RID 对应的 `ocgcore` 文件。

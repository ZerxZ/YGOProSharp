# YGOProSharp.Native

[English](#english) | [中文](#中文)

---

## English

### Overview

**YGOProSharp.Native** is the native runtime package for **YGOProSharp**.

This package provides prebuilt `ocgcore` native binaries for supported operating systems and CPU architectures. It is intended to be used by the managed YGOProSharp package or by .NET projects that need to load `ocgcore` through P/Invoke.

The native core is built from:

- `ygopro-core`: <https://github.com/Fluorohydride/ygopro-core>
- Lua: <https://www.lua.org/ftp/lua-5.4.8.tar.gz>

Project repository:

- <https://github.com/ZerxZ/YGOProSharp>

---

### Package ID

```text
YGOProSharp.Native
```

---

### Supported Target Frameworks

The NuGet project targets:

```text
net7.0
net8.0
net9.0
net10.0
```

The package mainly contains native runtime assets. It does not expose a high-level managed API by itself.

---

### Supported Runtime Identifiers

The package layout follows the standard .NET native runtime asset convention:

```text
runtimes/<rid>/native/
```

Currently planned runtime identifiers:

| RID | Native file |
|---|---|
| `win-x64` | `ocgcore.dll` |
| `win-arm64` | `ocgcore.dll` |
| `linux-x64` | `libocgcore.so` |
| `linux-arm64` | `libocgcore.so` |
| `osx-x64` | `libocgcore.dylib` |
| `osx-arm64` | `libocgcore.dylib` |

---

### Install

```bash
dotnet add package YGOProSharp.Native
```

If you are developing the main YGOProSharp managed wrapper, reference this package from the managed project:

```xml
<ItemGroup>
  <PackageReference Include="YGOProSharp.Native" Version="0.1.0" />
</ItemGroup>
```

---

### Native Library Loading

In .NET, native libraries placed under `runtimes/<rid>/native/` can be resolved automatically when the package is referenced.

For P/Invoke, use the platform-independent library name:

```csharp
using System.Runtime.InteropServices;

internal static partial class NativeMethods
{
    private const string LibraryName = "ocgcore";

    // Example only. Replace with actual exported functions from ocgcore.
    // [LibraryImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    // internal static partial int SomeNativeFunction();
}
```

On each platform, .NET will resolve the correct native file:

- Windows: `ocgcore.dll`
- Linux: `libocgcore.so`
- macOS: `libocgcore.dylib`

---

### Build Native Binaries Locally

This repository uses **xmake** to build `ocgcore`.

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
xmake -r ocgcore -y
```

Build for a specific platform and architecture:

```bash
# Windows x64
xmake f -c -p windows -a x64 -m release -y
xmake -r ocgcore -y

# Windows ARM64
xmake f -c -p windows -a arm64 -m release -y
xmake -r ocgcore -y

# Linux x64
xmake f -c -p linux -a x64 -m release -y
xmake -r ocgcore -y

# Linux ARM64
xmake f -c -p linux -a arm64 -m release -y
xmake -r ocgcore -y

# macOS x64
xmake f -c -p macosx -a x64 -m release -y
xmake -r ocgcore -y

# macOS ARM64
xmake f -c -p macosx -a arm64 -m release -y
xmake -r ocgcore -y
```

After building, native binaries are copied to:

```text
YGOProSharp.Native/lib/<rid>/
```

Example:

```text
YGOProSharp.Native/lib/win-x64/ocgcore.dll
YGOProSharp.Native/lib/linux-x64/libocgcore.so
YGOProSharp.Native/lib/osx-arm64/libocgcore.dylib
```

---

### Pack NuGet

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Release -o artifacts/nuget
```

For a prerelease package:

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Prerelease -o artifacts/nuget
```

The generated package will include native binaries under:

```text
runtimes/win-x64/native/
runtimes/win-arm64/native/
runtimes/linux-x64/native/
runtimes/linux-arm64/native/
runtimes/osx-x64/native/
runtimes/osx-arm64/native/
```

---

### GitHub Actions

The repository can build native binaries for multiple platforms using GitHub Actions.

Recommended build matrix:

- Windows x64
- Windows ARM64
- Linux x64
- Linux ARM64
- macOS x64
- macOS ARM64

Each native build uploads its `lib/<rid>/` output as an artifact. The packaging job downloads all native artifacts and runs `dotnet pack`.

---

### Troubleshooting

#### `LNK2001 unresolved external symbol lua_*`

This usually means the Lua ABI does not match `ygopro-core`.

For this project, Lua must be built locally in the same build configuration expected by `ocgcore`. Do not replace it with a random system Lua or a package-manager Lua unless the Lua headers and binary ABI are fully compatible.

#### `EntryPointNotFoundException`

Make sure `ocgcore` exports the expected native functions. On Windows, check the export table with:

```powershell
dumpbin /exports ocgcore.dll
```

If symbols are not exported, the xmake build may need explicit export handling.

#### Native library not found at runtime

Check that the NuGet package contains the correct layout:

```text
runtimes/<rid>/native/<native-library-file>
```

Also make sure your application is running with a supported RID.

---

### Credits

This package includes native binaries built from the open-source `ygopro-core` project:

- <https://github.com/Fluorohydride/ygopro-core>

Lua is provided by the Lua project:

- <https://www.lua.org/>

---

### License

This package follows the license terms of this repository and the upstream projects it uses.

Please review:

- `LICENSE` in this repository
- the license of `ygopro-core`
- the license of Lua

---

## 中文

### 概述

**YGOProSharp.Native** 是 **YGOProSharp** 的原生运行时包。

该包提供预构建的 `ocgcore` 原生二进制文件，面向不同操作系统和 CPU 架构。它主要供 YGOProSharp 的托管封装项目使用，也可以被需要通过 P/Invoke 加载 `ocgcore` 的 .NET 项目引用。

原生核心来源：

- `ygopro-core`: <https://github.com/Fluorohydride/ygopro-core>
- Lua: <https://www.lua.org/ftp/lua-5.4.8.tar.gz>

项目仓库：

- <https://github.com/ZerxZ/YGOProSharp>

---

### 包 ID

```text
YGOProSharp.Native
```

---

### 支持的目标框架

NuGet 项目目标框架：

```text
net7.0
net8.0
net9.0
net10.0
```

该包主要包含原生运行时资源，本身不提供高层托管 API。

---

### 支持的运行时标识符

该包使用标准 .NET 原生运行时资源布局：

```text
runtimes/<rid>/native/
```

当前计划支持的 RID：

| RID | 原生文件 |
|---|---|
| `win-x64` | `ocgcore.dll` |
| `win-arm64` | `ocgcore.dll` |
| `linux-x64` | `libocgcore.so` |
| `linux-arm64` | `libocgcore.so` |
| `osx-x64` | `libocgcore.dylib` |
| `osx-arm64` | `libocgcore.dylib` |

---

### 安装

```bash
dotnet add package YGOProSharp.Native
```

如果你正在开发 YGOProSharp 的托管封装项目，可以在托管项目中引用此包：

```xml
<ItemGroup>
  <PackageReference Include="YGOProSharp.Native" Version="0.1.0" />
</ItemGroup>
```

---

### 原生库加载

在 .NET 中，放置在 `runtimes/<rid>/native/` 下的原生库会在包被引用后由运行时自动解析。

P/Invoke 时建议使用跨平台库名：

```csharp
using System.Runtime.InteropServices;

internal static partial class NativeMethods
{
    private const string LibraryName = "ocgcore";

    // 仅作示例。请替换为 ocgcore 实际导出的函数。
    // [LibraryImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    // internal static partial int SomeNativeFunction();
}
```

在不同平台上，.NET 会解析到对应文件：

- Windows: `ocgcore.dll`
- Linux: `libocgcore.so`
- macOS: `libocgcore.dylib`

---

### 本地构建原生二进制

本仓库使用 **xmake** 构建 `ocgcore`。

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
xmake -r ocgcore -y
```

构建指定平台和架构：

```bash
# Windows x64
xmake f -c -p windows -a x64 -m release -y
xmake -r ocgcore -y

# Windows ARM64
xmake f -c -p windows -a arm64 -m release -y
xmake -r ocgcore -y

# Linux x64
xmake f -c -p linux -a x64 -m release -y
xmake -r ocgcore -y

# Linux ARM64
xmake f -c -p linux -a arm64 -m release -y
xmake -r ocgcore -y

# macOS x64
xmake f -c -p macosx -a x64 -m release -y
xmake -r ocgcore -y

# macOS ARM64
xmake f -c -p macosx -a arm64 -m release -y
xmake -r ocgcore -y
```

构建完成后，原生二进制会被复制到：

```text
YGOProSharp.Native/lib/<rid>/
```

示例：

```text
YGOProSharp.Native/lib/win-x64/ocgcore.dll
YGOProSharp.Native/lib/linux-x64/libocgcore.so
YGOProSharp.Native/lib/osx-arm64/libocgcore.dylib
```

---

### 打包 NuGet

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Release -o artifacts/nuget
```

预览版本打包：

```bash
dotnet pack YGOProSharp.Native/YGOProSharp.Native.csproj -c Prerelease -o artifacts/nuget
```

生成的 NuGet 包会包含如下原生库布局：

```text
runtimes/win-x64/native/
runtimes/win-arm64/native/
runtimes/linux-x64/native/
runtimes/linux-arm64/native/
runtimes/osx-x64/native/
runtimes/osx-arm64/native/
```

---

### GitHub Actions

仓库可以通过 GitHub Actions 构建多个平台的原生二进制。

推荐构建矩阵：

- Windows x64
- Windows ARM64
- Linux x64
- Linux ARM64
- macOS x64
- macOS ARM64

每个原生构建任务会上传对应的 `lib/<rid>/` 目录作为 artifact。打包任务会下载所有原生 artifact，然后执行 `dotnet pack`。

---

### 常见问题

#### `LNK2001 unresolved external symbol lua_*`

这通常表示 Lua ABI 与 `ygopro-core` 不匹配。

本项目中 Lua 应当在本地以 `ocgcore` 期望的方式编译。不要随意替换为系统 Lua 或包管理器中的 Lua，除非 Lua 头文件和二进制 ABI 完全兼容。

#### `EntryPointNotFoundException`

请确认 `ocgcore` 导出了预期的原生函数。在 Windows 上可以使用：

```powershell
dumpbin /exports ocgcore.dll
```

如果没有导出需要的符号，xmake 构建中可能需要增加显式导出处理。

#### 运行时找不到原生库

请检查 NuGet 包内是否包含正确布局：

```text
runtimes/<rid>/native/<native-library-file>
```

同时确认应用运行在受支持的 RID 上。

---

### 致谢

该包包含基于开源项目 `ygopro-core` 构建的原生二进制：

- <https://github.com/Fluorohydride/ygopro-core>

Lua 来自 Lua 项目：

- <https://www.lua.org/>

---

### 许可证

该包遵循本仓库以及所使用上游项目的许可证条款。

请查看：

- 本仓库中的 `LICENSE`
- `ygopro-core` 的许可证
- Lua 的许可证

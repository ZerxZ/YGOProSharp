# syntax=docker/dockerfile:1

# ==============================================================================
# Stage 1: native-builder — 提取或从源码重编 Linux libocgcore.so (x64 / arm64)
# ==============================================================================
FROM debian:bookworm-slim AS native-builder
ARG TARGETARCH=amd64
ARG BUILD_NATIVE_FROM_SOURCE=false

WORKDIR /native
COPY YGOProSharp.Native/lib/ ./lib/
COPY YGOProSharp.Native/xmake.lua ./xmake.lua

RUN set -eux; \
    case "${TARGETARCH}" in \
        arm64) RID="linux-arm64" ;; \
        *)     RID="linux-x64" ;; \
    esac; \
    mkdir -p /out; \
    if [ "${BUILD_NATIVE_FROM_SOURCE}" != "true" ] && [ -f "./lib/${RID}/libocgcore.so" ]; then \
        cp "./lib/${RID}/libocgcore.so" /out/libocgcore.so; \
    else \
        apt-get update && apt-get install -y --no-install-recommends \
            build-essential ca-certificates curl git && \
        curl -fsSL https://www.lua.org/ftp/lua-5.4.8.tar.gz -o /tmp/lua.tar.gz && \
        mkdir -p /tmp/lua && tar -xzf /tmp/lua.tar.gz -C /tmp/lua --strip-components=1 && \
        rm -f /tmp/lua/src/lua.c /tmp/lua/src/luac.c /tmp/lua/src/linit.c /tmp/lua/src/onelua.c && \
        git clone --depth 1 https://github.com/Fluorohydride/ygopro-core.git /tmp/ygopro-core && \
        g++ -O3 -fPIC -std=c++17 -DLUA_USE_LINUX -DOCGCORE_EXPORT_FUNCTIONS \
            -I/tmp/lua/src -I/tmp/ygopro-core \
            -x c++ /tmp/lua/src/*.c \
            -x c++ /tmp/ygopro-core/*.cpp \
            -shared -o /out/libocgcore.so -lm -ldl && \
        strip --strip-unneeded /out/libocgcore.so; \
    fi

# ==============================================================================
# Stage 2: web-builder — 构建前端 SPA（管理后台 + 玩家社区大厅）
# ==============================================================================
FROM node:22-alpine AS web-builder
WORKDIR /build/web

COPY web/package.json web/package-lock.json ./
RUN npm ci

COPY web/ ./
RUN npm run build && mkdir -p /web-out && cp -r /build/YGOProSharp.Web/wwwroot/* /web-out/

# ==============================================================================
# Stage 3: dotnet-builder — 还原并发布托管服务端与 CLI 组合根
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-builder
WORKDIR /src

COPY LICENSE ./
COPY YGOProSharp.Abstractions/YGOProSharp.Abstractions.csproj YGOProSharp.Abstractions/
COPY YGOProSharp.Core/YGOProSharp.Core.csproj YGOProSharp.Core/
COPY YGOProSharp.Protocol/YGOProSharp.Protocol.csproj YGOProSharp.Protocol/
COPY YGOProSharp.Native/YGOProSharp.Native.csproj YGOProSharp.Native/
COPY YGOProSharp.NativeApi/YGOProSharp.NativeApi.csproj YGOProSharp.NativeApi/
COPY YGOProSharp.Persistence/YGOProSharp.Persistence.csproj YGOProSharp.Persistence/
COPY YGOProSharp.Server/YGOProSharp.Server.csproj YGOProSharp.Server/
COPY YGOProSharp.WindBot/YGOProSharp.WindBot.csproj YGOProSharp.WindBot/
COPY YGOProSharp.AgentBridge/YGOProSharp.AgentBridge.csproj YGOProSharp.AgentBridge/
COPY YGOProSharp.Mcp/YGOProSharp.Mcp.csproj YGOProSharp.Mcp/
COPY YGOProSharp.Web/YGOProSharp.Web.csproj YGOProSharp.Web/
COPY YGOProSharp.Cli/YGOProSharp.Cli.csproj YGOProSharp.Cli/

RUN dotnet restore YGOProSharp.Cli/YGOProSharp.Cli.csproj

COPY YGOProSharp.Abstractions/ YGOProSharp.Abstractions/
COPY YGOProSharp.Core/ YGOProSharp.Core/
COPY YGOProSharp.Protocol/ YGOProSharp.Protocol/
COPY YGOProSharp.Native/ YGOProSharp.Native/
COPY YGOProSharp.NativeApi/ YGOProSharp.NativeApi/
COPY YGOProSharp.Persistence/ YGOProSharp.Persistence/
COPY YGOProSharp.Server/ YGOProSharp.Server/
COPY YGOProSharp.WindBot/ YGOProSharp.WindBot/
COPY YGOProSharp.AgentBridge/ YGOProSharp.AgentBridge/
COPY YGOProSharp.Mcp/ YGOProSharp.Mcp/
COPY YGOProSharp.Web/ YGOProSharp.Web/
COPY YGOProSharp.Cli/ YGOProSharp.Cli/
COPY --from=web-builder /web-out/ YGOProSharp.Web/wwwroot/

RUN dotnet publish YGOProSharp.Cli/YGOProSharp.Cli.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ==============================================================================
# Stage 4: runtime — 极简非 root 净化运行时镜像（零编译链、零源码、零 LLM 密钥）
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-bookworm-slim AS runtime
ARG TARGETARCH=amd64

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends libstdc++6 libsqlite3-0 curl; \
    rm -rf /var/lib/apt/lists/*; \
    groupadd --gid 10001 ygopro; \
    useradd --uid 10001 --gid ygopro --create-home --shell /usr/sbin/nologin ygopro; \
    mkdir -p /app/wwwroot \
             /app/data/base \
             /app/data/expansions \
             /app/data/db \
             /app/data/decks/random \
             /app/data/windbot/Decks/Lua \
             /app/data/diy \
             /app/data/replays \
             /app/data/logs; \
    chown -R ygopro:ygopro /app

WORKDIR /app

COPY --chown=ygopro:ygopro --from=dotnet-builder /app/publish/ ./
COPY --chown=ygopro:ygopro --from=web-builder /web-out/ ./wwwroot/
COPY --chown=ygopro:ygopro --from=native-builder /out/libocgcore.so ./libocgcore.so

RUN set -eux; \
    case "${TARGETARCH}" in \
        arm64) RID="linux-arm64" ;; \
        *)     RID="linux-x64" ;; \
    esac; \
    mkdir -p "/app/runtimes/${RID}/native"; \
    cp /app/libocgcore.so "/app/runtimes/${RID}/native/libocgcore.so"; \
    chown -R ygopro:ygopro /app/runtimes

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    YGOPRO_ROOT_PATH=/app/data \
    YGOPRO_GAME_PORT=7911 \
    YGOPRO_ENABLE_WEB=true \
    YGOPRO_WEB_HOST=0.0.0.0 \
    YGOPRO_WEB_PORT=7912 \
    YGOPRO_MULTI_ROOM=true \
    YGOPRO_DB_PROVIDER=Sqlite \
    YGOPRO_DB_CONNECTION_STRING="Data Source=/app/data/db/ygoprosharp.db"

EXPOSE 7911 7912

USER ygopro

HEALTHCHECK --interval=20s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS "http://127.0.0.1:${YGOPRO_WEB_PORT:-7912}/healthz" || exit 1

ENTRYPOINT ["dotnet", "/app/YGOProSharp.Cli.dll", "server"]

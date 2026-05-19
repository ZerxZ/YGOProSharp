-- xmake.lua
add_rules("mode.debug", "mode.release")
set_languages("c11", "cxx17")

if is_plat("windows") then
    if is_mode("release") then
        set_runtimes("MT")
    else
        set_runtimes("MTd")
    end
end

local lua_version = "5.4.8"
local lua_url = "https://www.lua.org/ftp/lua-" .. lua_version .. ".tar.gz"
local deps_dir = path.join(os.projectdir(), ".deps")
local lua_root = path.join(deps_dir, "lua-" .. lua_version)
local lua_src = path.join(lua_root, "src")
local lua_archive = path.join(deps_dir, "lua-" .. lua_version .. ".tar.gz")

local function native_rid()
    local plat
    if is_plat("windows") then
        plat = "win"
    elseif is_plat("macosx") then
        plat = "osx"
    elseif is_plat("linux") then
        plat = "linux"
    else
        plat = "$(plat)"
    end

    local arch
    if is_arch("x64", "x86_64", "amd64") then
        arch = "x64"
    elseif is_arch("arm64", "aarch64") then
        arch = "arm64"
    else
        arch = "$(arch)"
    end

    return plat .. "-" .. arch
end

local function native_outdir()
    return path.join(os.projectdir(), "lib", native_rid())
end

target("lua")
    set_kind("static")

    on_load(function (target)
        import("net.http")
        import("utils.archive")

        if not os.isdir(lua_src) then
            os.mkdir(deps_dir)

            if not os.isfile(lua_archive) then
                http.download(lua_url, lua_archive)
            end

            local extract_dir = path.join(deps_dir, "_lua_extract")
            os.rm(extract_dir)
            os.mkdir(extract_dir)

            archive.extract(lua_archive, extract_dir)

            os.rm(lua_root)
            os.mv(path.join(extract_dir, "lua-" .. lua_version), lua_root)
            os.rm(extract_dir)
        end

        target:add("includedirs", lua_src, {public = true})

        -- 对齐原 Premake: compileas "C++"
        target:add("files", path.join(lua_src, "*.c"), {sourcekind = "cxx"})

        -- 对齐原 Premake removefiles
        target:add("remove_files",
            path.join(lua_src, "lua.c"),
            path.join(lua_src, "luac.c"),
            path.join(lua_src, "linit.c"),
            path.join(lua_src, "onelua.c")
        )
    end)

    if is_plat("windows") then
        add_cxxflags("/EHsc")
        add_defines("_CRT_SECURE_NO_WARNINGS")
    else
        add_cxxflags("-fPIC", {force = true})
    end

    if is_mode("debug") then
        add_defines("LUA_USE_APICHECK")
    end

    if is_plat("bsd") then
        add_defines("LUA_USE_POSIX")
    elseif is_plat("macosx") then
        add_defines("LUA_USE_MACOSX")
    elseif is_plat("linux") then
        add_defines("LUA_USE_LINUX")
    end

target("ocgcore")
    set_kind("shared")
    add_deps("lua")

    on_load(function (target)
        import("devel.git")

        local ocg_dir = path.join(os.projectdir(), "ygopro-core")
        if not os.isdir(ocg_dir) then
            git.clone("https://github.com/Fluorohydride/ygopro-core.git", {
                outputdir = ocg_dir,
                depth = 1
            })
        end

        target:add("files", path.join(ocg_dir, "*.cpp"))
        target:add("includedirs", ocg_dir)
    end)

    if is_mode("release") then
        set_optimize("fastest")
        if is_plat("windows") then
            set_policy("build.optimization.lto", true)
            add_cxflags("/wd4334")
        end
    end

    if is_plat("windows") then
        add_cxflags("/utf-8", "/permissive-")
        add_defines("_CRT_SECURE_NO_WARNINGS")
    elseif is_plat("linux") then
        add_syslinks("m", "dl")
    end

    after_build(function (target)
        local outdir = native_outdir()
        os.mkdir(outdir)

        os.cp(target:targetfile(), outdir)

        if is_plat("windows") then
            local name = path.basename(target:targetfile())
            os.trycp(path.join(target:targetdir(), name .. ".lib"), outdir)
            os.trycp(path.join(target:targetdir(), name .. ".pdb"), outdir)
        end
    end)
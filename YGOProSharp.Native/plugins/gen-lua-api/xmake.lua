task("gen-lua-api")
    set_category("plugin")
    set_menu {
        usage = "xmake gen-lua-api [options]",
        description = "Generate ygopro-core LuaLS API files.",
        options = {
            {"c", "core", "kv", nil, "Set ygopro-core source directory."},
            {"o", "out", "kv", nil, "Set lua-api output directory."},
            {nil, "check", "k", nil, "Check generated files without writing."}
        }
    }
    on_run(function ()
        import("core.base.option")

        local project_dir = os.projectdir()
        local generator = import("generator", {rootdir = path.join(project_dir, "lua-api"), anonymous = true})
        generator.run({
            project_dir = project_dir,
            core_dir = option.get("core"),
            out_dir = option.get("out"),
            check = option.get("check"),
            verbose = option.get("verbose")
        })
    end)

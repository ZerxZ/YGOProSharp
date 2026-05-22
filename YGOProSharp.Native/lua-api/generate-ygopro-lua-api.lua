local function script_dir()
    if os and os.scriptdir then
        return os.scriptdir()
    end

    local source = debug.getinfo(1, "S").source
    if source:sub(1, 1) == "@" then
        source = source:sub(2)
    end
    return source:match("^(.*)[/\\][^/\\]+$") or "."
end

local function path_join(...)
    if path and path.join then
        return path.join(...)
    end

    local sep = (package and package.config and package.config:sub(1, 1)) or "/"
    local parts = {...}
    local result = tostring(parts[1] or "")
    for i = 2, #parts do
        local part = tostring(parts[i] or "")
        if part ~= "" then
            if result == "" then
                result = part
            elseif result:sub(-1) == "/" or result:sub(-1) == "\\" then
                result = result .. part
            else
                result = result .. sep .. part
            end
        end
    end
    return result
end

local function default_project_dir()
    return path_join(script_dir(), "..")
end

local function print_help()
    print("Usage: xmake lua YGOProSharp.Native/lua-api/generate-ygopro-lua-api.lua [options]")
    print("")
    print("Options:")
    print("  --project=PATH   Set YGOProSharp.Native project directory.")
    print("  --core=PATH      Set ygopro-core source directory.")
    print("  --out=PATH       Set lua-api output directory.")
    print("  --check          Check generated files without writing.")
    print("  --verbose, -v    Print generation counts.")
    print("  --help, -h       Print this help message.")
end

local function parse_args(argv)
    local args = {
        project = default_project_dir(),
        check = false,
        verbose = false
    }

    local i = 1
    while i <= #argv do
        local raw = argv[i]
        if raw == nil then
            i = i + 1
            goto continue
        end

        local item = tostring(raw)
        local name, value = item:match("^%-%-([^=]+)=(.*)$")
        if name then
            args[name] = value
        elseif item == "--project" or item == "--core" or item == "--out" then
            local key = item:sub(3)
            i = i + 1
            args[key] = argv[i]
        elseif item == "--check" then
            args.check = true
        elseif item == "--verbose" or item == "-v" then
            args.verbose = true
        elseif item == "--help" or item == "-h" then
            args.help = true
        else
            raise("unknown argument: %s", item)
        end

        i = i + 1
        ::continue::
    end

    return args
end

function main(...)
    local args = parse_args({...})
    if args.help then
        print_help()
        return
    end

    local project_dir = args.project
    local generator = import("generator", {rootdir = path_join(project_dir, "lua-api"), anonymous = true})
    generator.run({
        project_dir = project_dir,
        core_dir = args.core,
        out_dir = args.out,
        check = args.check,
        verbose = args.verbose
    })
end

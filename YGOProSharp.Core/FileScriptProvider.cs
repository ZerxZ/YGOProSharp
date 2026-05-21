using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.Core;

public sealed class FileScriptProvider : IScriptProvider
{
    private readonly string _rootPath;
    private readonly string _scriptDirectory;

    public FileScriptProvider(string rootPath, string scriptDirectory)
    {
        _rootPath = rootPath;
        _scriptDirectory = scriptDirectory;
    }

    public bool TryGetScript(string scriptName, out byte[] script)
    {
        string filename = Path.Combine(_rootPath, scriptName.Replace("./script", _scriptDirectory));
        if (!File.Exists(filename))
        {
            script = [];
            return false;
        }

        script = File.ReadAllBytes(filename);
        return true;
    }
}

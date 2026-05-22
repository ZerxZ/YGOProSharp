using WindBot.Game;

namespace WindBot;

public static class WindBotRuntime
{
    public static Random Random { get; private set; } = new Random();

    public static void Configure(WindBotRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Random = options.Random ?? new Random();
        CardDatabase.Initialize(options.CardRepository);
    }

    public static FileStream ReadFile(string directory, string filename, string extension)
    {
        string tryfilename = filename + "." + extension;
        string fullpath = Path.Combine(directory, tryfilename);
        if (!File.Exists(fullpath))
            fullpath = filename;
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("../", filename);
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("../deck/", filename);
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("../", tryfilename);
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("../deck/", tryfilename);
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("Data/WindBot/" + directory, tryfilename);
        if (!File.Exists(fullpath))
            fullpath = Path.Combine("Deck/", tryfilename);
        return new FileStream(fullpath, FileMode.Open, FileAccess.Read);
    }
}

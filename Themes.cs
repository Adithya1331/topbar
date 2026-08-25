namespace TopBar;

internal sealed class Theme
{
    public uint Grade0;
    public uint Grade1;
    public uint Grade2;
    public uint Grade3;
    public uint Grade4;
    public uint Meta;
    public uint Text;
}

internal static class Themes
{
    public static readonly (string Key, string Label)[] List =
    [
        ("standard", "Monkeytype"),
        ("githubDark", "GitHub Green"),
        ("halloween", "Halloween"),
        ("teal", "Teal"),
        ("leftPad", "@left_pad"),
        ("dracula", "Dracula"),
        ("blue", "Blue"),
        ("panda", "Panda"),
        ("sunny", "Sunny"),
        ("pink", "Pink"),
        ("solarizedDark", "Solarized Dark"),
        ("solarizedLight", "Solarized Light"),
    ];

    private static readonly Dictionary<string, Theme> All = new()
    {
        ["standard"] = Make("#000000", "#666666", "#ebedf0", "#fae588", "#f5d65b", "#f0c730", "#e2b714"),
        ["githubDark"] = Make("#ffffff", "#dddddd", "#161b22", "#003820", "#00602d", "#10983d", "#27d545"),
        ["halloween"] = Make("#000000", "#666666", "#ebedf0", "#FFEE4A", "#FFC501", "#FE9600", "#03001C"),
        ["teal"] = Make("#000000", "#666666", "#ebedf0", "#7FFFD4", "#76EEC6", "#66CDAA", "#458B74"),
        ["leftPad"] = Make("#ffffff", "#999999", "#2F2F2F", "#646464", "#A5A5A5", "#DDDDDD", "#F6F6F6"),
        ["dracula"] = Make("#f8f8f2", "#666666", "#282a36", "#44475a", "#6272a4", "#bd93f9", "#ff79c6"),
        ["blue"] = Make("#C0C0C0", "#666666", "#222222", "#263342", "#344E6C", "#416895", "#4F83BF"),
        ["panda"] = Make("#E6E6E6", "#676B79", "#242526", "#34353B", "#6FC1FF", "#19f9d8", "#FF4B82"),
        ["sunny"] = Make("#000000", "#666666", "#fff9ae", "#f8ed62", "#e9d700", "#dab600", "#a98600"),
        ["pink"] = Make("#000000", "#666666", "#ebedf0", "#e48bdc", "#ca5bcc", "#a74aa8", "#61185f"),
        ["solarizedDark"] = Make("#93a1a1", "#586e75", "#073642", "#268bd2", "#2aa198", "#b58900", "#d33682"),
        ["solarizedLight"] = Make("#586e75", "#93a1a1", "#eee8d5", "#b58900", "#cb4b16", "#dc322f", "#6c71c4"),
    };

    public static Theme Get(int index)
    {
        if (index < 0 || index >= List.Length) return All["standard"];
        return All[List[index].Key];
    }

    public static uint GradeColor(Theme t, int count) => count switch
    {
        0 => t.Grade0,
        < 3 => t.Grade1,
        < 6 => t.Grade2,
        < 11 => t.Grade3,
        _ => t.Grade4,
    };

    private static Theme Make(string text, string meta, string g0, string g1, string g2, string g3, string g4) => new()
    {
        Text = Hex(text),
        Meta = Hex(meta),
        Grade0 = Hex(g0),
        Grade1 = Hex(g1),
        Grade2 = Hex(g2),
        Grade3 = Hex(g3),
        Grade4 = Hex(g4),
    };

    private static uint Hex(string h)
    {
        uint v = Convert.ToUInt32(h.TrimStart('#'), 16);
        int r = (int)((v >> 16) & 0xFF);
        int g = (int)((v >> 8) & 0xFF);
        int b = (int)(v & 0xFF);
        return (uint)(r | (g << 8) | (b << 16));
    }
}

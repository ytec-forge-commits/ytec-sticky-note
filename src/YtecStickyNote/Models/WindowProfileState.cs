namespace YtecStickyNote.Models;

public sealed class WindowProfileState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<WindowProfile> Profiles { get; set; } = [];
}

public sealed class WindowProfile
{
    public string LayoutId { get; set; } = string.Empty;

    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 520;

    public double Height { get; set; } = 620;

    public WindowStateData ToWindowStateData() => new()
    {
        Left = Left,
        Top = Top,
        Width = Width,
        Height = Height
    };

    public static WindowProfile From(string layoutId, WindowStateData placement) => new()
    {
        LayoutId = layoutId,
        Left = placement.Left,
        Top = placement.Top,
        Width = placement.Width,
        Height = placement.Height
    };
}

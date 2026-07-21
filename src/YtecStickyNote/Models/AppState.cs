namespace YtecStickyNote.Models;

public sealed class AppState
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public string RichTextRtfBase64 { get; set; } = string.Empty;

    public string RichTextXamlPackageBase64 { get; set; } = string.Empty;

    public string PlainText { get; set; } = string.Empty;

    public string ThemeId { get; set; } = "lemon";

    public bool AlwaysOnTop { get; set; }

    public WindowStateData Window { get; set; } = new();

    public DateTimeOffset LastSavedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class WindowStateData
{
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 520;

    public double Height { get; set; } = 620;
}

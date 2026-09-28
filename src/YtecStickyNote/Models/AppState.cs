using System.Text.Json;
using System.Text.Json.Serialization;

namespace YtecStickyNote.Models;

public sealed class AppState : IJsonOnDeserialized
{
    public const int CurrentVersion = 3;
    public const int MaximumPageCount = 1000;
    public const long MaximumTotalContentCharacters = 48L * 1024 * 1024;

    public int Version { get; set; } = CurrentVersion;

    public string RichTextRtfBase64 { get; set; } = string.Empty;

    public string RichTextXamlPackageBase64 { get; set; } = string.Empty;

    public string PlainText { get; set; } = string.Empty;

    public string ThemeId { get; set; } = "lemon";

    /// <summary>
    /// Page-specific note data. The legacy root fields above remain so that a v1/v2
    /// document can be copied into the first page before its first v3 save.
    /// </summary>
    public List<NotePageState> Pages { get; set; } = [];

    /// <summary>
    /// Stable identifier of the currently displayed page. An index would point to
    /// the wrong note after insertion or deletion.
    /// </summary>
    public string? CurrentPageId { get; set; }

    public bool AlwaysOnTop { get; set; }

    public WindowStateData Window { get; set; } = new();

    public DateTimeOffset LastSavedAt { get; set; } = DateTimeOffset.Now;

    public NotePageState GetCurrentPage()
    {
        NormalizePages();
        return Pages.First(page => string.Equals(page.Id, CurrentPageId, StringComparison.Ordinal));
    }

    public int GetCurrentPageIndex()
    {
        NormalizePages();
        return Pages.FindIndex(page => string.Equals(page.Id, CurrentPageId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Makes page data safe to use after deserialization while retaining every
    /// non-null page. A null page element is deliberately rejected because it is
    /// ambiguous whether note data has already been lost; the caller must then
    /// leave the source file untouched.
    /// </summary>
    public void NormalizePages()
    {
        if (Pages is null || Pages.Count == 0)
        {
            Pages = [CreatePageFromLegacyFields()];
        }

        ValidateResourceLimits();

        if (Pages.Any(page => page is null))
        {
            throw new JsonException("ページ一覧に壊れたページが含まれています。");
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in Pages)
        {
            var identifier = page.Id?.Trim();
            if (string.IsNullOrWhiteSpace(identifier) || !identifiers.Add(identifier))
            {
                do
                {
                    identifier = Guid.NewGuid().ToString("N");
                }
                while (!identifiers.Add(identifier));
            }

            page.Id = identifier;
            page.RichTextRtfBase64 ??= string.Empty;
            page.RichTextXamlPackageBase64 ??= string.Empty;
            page.PlainText ??= string.Empty;
            page.ThemeId = NoteTheme.Find(page.ThemeId).Id;
        }

        if (string.IsNullOrWhiteSpace(CurrentPageId) || !identifiers.Contains(CurrentPageId))
        {
            CurrentPageId = Pages[0].Id;
        }
    }

    public void ValidateResourceLimits()
    {
        if (Pages is not null && Pages.Count > MaximumPageCount)
        {
            throw new JsonException($"ページ数が上限（{MaximumPageCount}ページ）を超えています。");
        }

        long totalCharacters = 0;
        if (Pages is null || Pages.Count == 0)
        {
            AddLength(RichTextRtfBase64);
            AddLength(RichTextXamlPackageBase64);
            AddLength(PlainText);
        }
        else
        {
            foreach (var page in Pages)
            {
                if (page is null)
                {
                    throw new JsonException("ページ一覧に壊れたページが含まれています。");
                }

                AddLength(page.RichTextRtfBase64);
                AddLength(page.RichTextXamlPackageBase64);
                AddLength(page.PlainText);
            }
        }

        return;

        void AddLength(string? value)
        {
            totalCharacters = checked(totalCharacters + (value?.Length ?? 0));
            if (totalCharacters > MaximumTotalContentCharacters)
            {
                throw new JsonException("保存する本文データが大きすぎます。");
            }
        }
    }

    public void MirrorCurrentPageToLegacyFields()
    {
        var page = GetCurrentPage();
        RichTextRtfBase64 = page.RichTextRtfBase64;
        RichTextXamlPackageBase64 = page.RichTextXamlPackageBase64;
        PlainText = page.PlainText;
        ThemeId = page.ThemeId;
    }

    void IJsonOnDeserialized.OnDeserialized() => NormalizePages();

    private NotePageState CreatePageFromLegacyFields() => new()
    {
        RichTextRtfBase64 = RichTextRtfBase64 ?? string.Empty,
        RichTextXamlPackageBase64 = RichTextXamlPackageBase64 ?? string.Empty,
        PlainText = PlainText ?? string.Empty,
        ThemeId = NoteTheme.Find(ThemeId).Id
    };
}

public sealed class NotePageState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string RichTextRtfBase64 { get; set; } = string.Empty;

    public string RichTextXamlPackageBase64 { get; set; } = string.Empty;

    public string PlainText { get; set; } = string.Empty;

    public string ThemeId { get; set; } = "lemon";
}

public sealed class WindowStateData
{
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; } = 520;

    public double Height { get; set; } = 620;
}

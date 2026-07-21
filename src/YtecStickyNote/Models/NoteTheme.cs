using System.Windows.Media;

namespace YtecStickyNote.Models;

public sealed record NoteTheme(
    string Id,
    string DisplayName,
    Color Paper,
    Color Rule,
    Color Margin,
    Color Chrome,
    Color Accent)
{
    public static IReadOnlyList<NoteTheme> All { get; } =
    [
        new("lemon", "レモン", Color.FromRgb(255, 249, 205), Color.FromRgb(156, 190, 205), Color.FromRgb(229, 144, 144), Color.FromRgb(251, 239, 166), Color.FromRgb(194, 152, 50)),
        new("sakura", "さくら", Color.FromRgb(255, 239, 244), Color.FromRgb(213, 179, 198), Color.FromRgb(228, 150, 169), Color.FromRgb(250, 218, 227), Color.FromRgb(190, 98, 126)),
        new("mint", "ミント", Color.FromRgb(238, 249, 237), Color.FromRgb(164, 199, 180), Color.FromRgb(221, 165, 145), Color.FromRgb(215, 239, 218), Color.FromRgb(76, 143, 106)),
        new("sky", "スカイ", Color.FromRgb(237, 247, 255), Color.FromRgb(151, 185, 213), Color.FromRgb(231, 167, 153), Color.FromRgb(211, 234, 250), Color.FromRgb(72, 132, 178)),
        new("ivory", "アイボリー", Color.FromRgb(250, 245, 234), Color.FromRgb(185, 180, 166), Color.FromRgb(216, 157, 138), Color.FromRgb(237, 229, 210), Color.FromRgb(137, 113, 76)),
        new("lavender", "ラベンダー", Color.FromRgb(245, 240, 255), Color.FromRgb(183, 172, 213), Color.FromRgb(226, 161, 173), Color.FromRgb(229, 216, 248), Color.FromRgb(130, 106, 164)),
        new("peach", "ピーチ", Color.FromRgb(255, 240, 230), Color.FromRgb(212, 181, 159), Color.FromRgb(222, 154, 142), Color.FromRgb(246, 213, 189), Color.FromRgb(185, 113, 71)),
        new("aqua", "アクア", Color.FromRgb(234, 249, 248), Color.FromRgb(156, 200, 197), Color.FromRgb(223, 158, 152), Color.FromRgb(207, 239, 235), Color.FromRgb(61, 143, 137)),
        new("gray", "グレー", Color.FromRgb(243, 244, 245), Color.FromRgb(181, 187, 193), Color.FromRgb(212, 154, 154), Color.FromRgb(225, 228, 231), Color.FromRgb(101, 113, 123)),
        new("mocha", "モカ", Color.FromRgb(244, 236, 228), Color.FromRgb(197, 179, 164), Color.FromRgb(217, 155, 145), Color.FromRgb(229, 211, 194), Color.FromRgb(138, 102, 81))
    ];

    public static NoteTheme Find(string? id) =>
        All.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

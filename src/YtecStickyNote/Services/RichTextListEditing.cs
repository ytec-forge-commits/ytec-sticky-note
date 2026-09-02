using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace YtecStickyNote.Services;

public static class RichTextListEditing
{
    public static bool TryExitEmptyListItem(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var paragraph = GetListParagraphAtCaret(editor.Selection.Start);
        if (!editor.Selection.IsEmpty ||
            paragraph is null ||
            HasUserContent(paragraph.Inlines))
        {
            return false;
        }

        return RemoveCurrentListMarker(editor, paragraph);
    }

    public static bool TryRemoveListMarkerAtItemStart(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var paragraph = GetListParagraphAtCaret(editor.Selection.Start);
        var caret = editor.Selection.Start.GetInsertionPosition(LogicalDirection.Forward);
        var paragraphStart = paragraph?.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        if (!editor.Selection.IsEmpty ||
            paragraph is null ||
            caret is null ||
            paragraphStart is null ||
            paragraphStart.CompareTo(caret) != 0)
        {
            return false;
        }

        return RemoveCurrentListMarker(editor, paragraph);
    }

    private static bool RemoveCurrentListMarker(RichTextBox editor, Paragraph paragraph)
    {
        var list = FindContainingList(paragraph);
        if (list is null)
        {
            return false;
        }

        editor.BeginChange();
        try
        {
            editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
            if (IsUnordered(list.MarkerStyle))
            {
                EditingCommands.ToggleBullets.Execute(null, editor);
            }
            else
            {
                EditingCommands.ToggleNumbering.Execute(null, editor);
            }

            var caret = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward) ?? paragraph.ContentStart;
            editor.Selection.Select(caret, caret);
            editor.CaretPosition = caret;
        }
        finally
        {
            editor.EndChange();
        }

        return true;
    }

    private static List? FindContainingList(Paragraph paragraph)
    {
        if (paragraph.Parent is ListItem { Parent: List directList })
        {
            return directList;
        }

        TextElement? current = paragraph;
        while (current?.Parent is TextElement parent)
        {
            if (parent is List list)
            {
                return list;
            }

            current = parent;
        }

        return null;
    }

    private static Paragraph? GetListParagraphAtCaret(TextPointer caret)
    {
        if (caret.Paragraph is { Parent: ListItem } paragraph)
        {
            return paragraph;
        }

        var next = caret.GetNextInsertionPosition(LogicalDirection.Forward)?.Paragraph;
        if (next?.Parent is ListItem)
        {
            return next;
        }

        return null;
    }

    private static bool IsUnordered(TextMarkerStyle style) =>
        style >= TextMarkerStyle.Disc && style <= TextMarkerStyle.Box;

    private static bool HasUserContent(InlineCollection inlines)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run { Text.Length: > 0 }:
                case LineBreak:
                case InlineUIContainer:
                    return true;
                case Span span when HasUserContent(span.Inlines):
                    return true;
            }
        }

        return false;
    }
}

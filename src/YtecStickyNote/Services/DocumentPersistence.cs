using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace YtecStickyNote.Services;

public sealed record DocumentSnapshot(string XamlPackageBase64, string RtfBase64, string PlainText);

public enum DocumentRestoreResult
{
    Empty,
    XamlPackage,
    RtfFallback,
    Failed
}

public static class DocumentPersistence
{
    public static DocumentSnapshot Capture(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var range = new TextRange(document.ContentStart, document.ContentEnd);
        var xamlPackageBase64 = TrySave(range, DataFormats.XamlPackage);
        var rtfBase64 = Save(range, DataFormats.Rtf);
        var plainText = range.Text.TrimEnd('\r', '\n');
        return new DocumentSnapshot(xamlPackageBase64, rtfBase64, plainText);
    }

    public static DocumentRestoreResult Restore(
        FlowDocument document,
        string? xamlPackageBase64,
        string? rtfBase64)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrWhiteSpace(xamlPackageBase64) &&
            TryLoad(document, xamlPackageBase64, DataFormats.XamlPackage))
        {
            return DocumentRestoreResult.XamlPackage;
        }

        if (!string.IsNullOrWhiteSpace(rtfBase64) &&
            TryLoad(document, rtfBase64, DataFormats.Rtf))
        {
            return DocumentRestoreResult.RtfFallback;
        }

        Reset(document);
        return string.IsNullOrWhiteSpace(xamlPackageBase64) && string.IsNullOrWhiteSpace(rtfBase64)
            ? DocumentRestoreResult.Empty
            : DocumentRestoreResult.Failed;
    }

    private static string Save(TextRange range, string format)
    {
        using var stream = new MemoryStream();
        range.Save(stream, format);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static string TrySave(TextRange range, string format)
    {
        try
        {
            return Save(range, format);
        }
        catch (Exception ex) when (IsDocumentFormatException(ex))
        {
            return string.Empty;
        }
    }

    private static bool TryLoad(FlowDocument document, string base64, string format)
    {
        try
        {
            Reset(document);
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, format);
            return true;
        }
        catch (Exception ex) when (IsDocumentFormatException(ex))
        {
            return false;
        }
    }

    private static void Reset(FlowDocument document)
    {
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph());
    }

    private static bool IsDocumentFormatException(Exception exception) =>
        exception is FormatException or ArgumentException or IOException or InvalidOperationException or
            NotSupportedException or FileFormatException or XamlParseException;
}

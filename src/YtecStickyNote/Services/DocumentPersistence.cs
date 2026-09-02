using System.IO;
using System.IO.Compression;
using System.Xml;
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
    public const int MaximumDecodedDocumentBytes = 16 * 1024 * 1024;
    private const int MaximumPackageEntries = 256;
    private const int MaximumDocumentNodes = 100_000;
    private const int MaximumDocumentDepth = 128;
    private const int MaximumBase64Characters = ((MaximumDecodedDocumentBytes + 2) / 3) * 4;

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
        if (stream.Length > MaximumDecodedDocumentBytes)
        {
            throw new InvalidDataException("本文データが大きすぎるため保存できません。");
        }
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
            if (!IsBase64WithinLimit(base64))
            {
                return false;
            }

            Reset(document);
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length > MaximumDecodedDocumentBytes)
            {
                return false;
            }

            if (string.Equals(format, DataFormats.XamlPackage, StringComparison.Ordinal))
            {
                ValidateXamlPackage(bytes);
            }

            using var stream = new MemoryStream(bytes);
            new TextRange(document.ContentStart, document.ContentEnd).Load(stream, format);
            ValidateDocumentComplexity(document);
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

    private static bool IsBase64WithinLimit(string base64)
    {
        if (base64.Length > MaximumBase64Characters + 4096)
        {
            return false;
        }

        var significantCharacters = 0;
        foreach (var character in base64)
        {
            if (!char.IsWhiteSpace(character) && ++significantCharacters > MaximumBase64Characters)
            {
                return false;
            }
        }
        return true;
    }

    private static void ValidateXamlPackage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumPackageEntries)
        {
            throw new InvalidDataException("本文パッケージの項目数が上限を超えています。");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalExpandedBytes = 0;
        var totalMarkupNodes = 0;
        var buffer = new byte[64 * 1024];
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName) ||
                entry.FullName.StartsWith("/", StringComparison.Ordinal) ||
                entry.FullName.Contains('\\') ||
                entry.FullName.Contains(':') ||
                entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(part => part is "." or "..") ||
                !names.Add(entry.FullName) ||
                entry.Length > MaximumDecodedDocumentBytes)
            {
                throw new InvalidDataException("本文パッケージに不正または過大な項目があります。");
            }

            long entryBytes = 0;
            using var entryStream = entry.Open();
            while (true)
            {
                var read = entryStream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                entryBytes = checked(entryBytes + read);
                totalExpandedBytes = checked(totalExpandedBytes + read);
                if (entryBytes > MaximumDecodedDocumentBytes || totalExpandedBytes > MaximumDecodedDocumentBytes)
                {
                    throw new InvalidDataException("本文パッケージの展開後サイズが上限を超えています。");
                }
            }

            if (IsMarkupPart(entry.FullName))
            {
                ValidatePackageMarkup(entry, ref totalMarkupNodes);
            }
        }
    }

    private static bool IsMarkupPart(string name) =>
        name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static void ValidatePackageMarkup(ZipArchiveEntry entry, ref int totalMarkupNodes)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDecodedDocumentBytes,
            MaxCharactersFromEntities = 0,
            CloseInput = false
        };

        using var entryStream = entry.Open();
        using var reader = XmlReader.Create(entryStream, settings);
        while (reader.Read())
        {
            if (++totalMarkupNodes > MaximumDocumentNodes || reader.Depth > MaximumDocumentDepth)
            {
                throw new InvalidDataException("本文パッケージのXML構造が複雑すぎます。");
            }
        }
    }

    private static void ValidateDocumentComplexity(FlowDocument document)
    {
        var nodes = 0;
        var pending = new Stack<(DependencyObject Node, int Depth)>();
        pending.Push((document, 0));
        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            if (++nodes > MaximumDocumentNodes || depth > MaximumDocumentDepth)
            {
                throw new InvalidDataException("本文の構造が複雑すぎます。");
            }

            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                pending.Push((child, depth + 1));
            }
        }
    }

    private static bool IsDocumentFormatException(Exception exception) =>
        exception is FormatException or ArgumentException or IOException or InvalidDataException or InvalidOperationException or XmlException or
            NotSupportedException or FileFormatException or XamlParseException;
}

using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Text.Json;

namespace YtecStickyNote.Services;

internal sealed record VerifiedStartupRuntimeManifest(
    IReadOnlyList<string> RuntimeFileNames,
    string ManifestDigest);

internal static class StartupRuntimeManifest
{
    public const string ManifestFileName = "startup-runtime-manifest.json";
    public const string SignatureFileName = "startup-runtime-manifest.p7s";

    private const int CurrentFormat = 1;
    private const int MaximumManifestBytes = 4 * 1024 * 1024;
    private const int MaximumSignatureBytes = 1024 * 1024;
    private const int MaximumRuntimeFiles = 1024;
    private static readonly HashSet<string> RuntimeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".json", ".dat"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static VerifiedStartupRuntimeManifest VerifyDirectory(
        string directory,
        ReadOnlySpan<byte> expectedSignerCertificate,
        bool allowUnrelatedSourceFiles)
    {
        var fullDirectory = Path.GetFullPath(directory);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"自動起動用の実行フォルダーが見つかりません: {fullDirectory}");
        }

        var manifestPath = Path.Combine(fullDirectory, ManifestFileName);
        var signaturePath = Path.Combine(fullDirectory, SignatureFileName);
        var manifestBytes = ReadBoundedFile(manifestPath, MaximumManifestBytes, "自動起動マニフェスト");
        var signatureBytes = ReadBoundedFile(signaturePath, MaximumSignatureBytes, "自動起動マニフェスト署名");
        VerifySignature(manifestBytes, signatureBytes, expectedSignerCertificate);

        var manifest = JsonSerializer.Deserialize<RuntimeManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("自動起動マニフェストを読み取れません。");
        var runningVersion = typeof(StartupRuntimeManifest).Assembly.GetName().Version;
        if (manifest.Format != CurrentFormat ||
            !string.Equals(manifest.Product, "Keisai", StringComparison.Ordinal) ||
            !Version.TryParse(manifest.Version, out var manifestVersion) ||
            runningVersion is null ||
            manifestVersion.Major != runningVersion.Major ||
            manifestVersion.Minor != runningVersion.Minor ||
            manifestVersion.Build != runningVersion.Build ||
            manifest.Files is null ||
            manifest.Files.Count == 0 || manifest.Files.Count > MaximumRuntimeFiles)
        {
            throw new InvalidDataException("自動起動マニフェストの形式またはファイル数が不正です。");
        }

        var expectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Files)
        {
            ValidateEntry(entry, expectedNames);
        }

        var actualRuntimeFiles = Directory.EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => RuntimeExtensions.Contains(Path.GetExtension(path)))
            .Where(path => !string.Equals(Path.GetFileName(path), ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase);
        if (actualRuntimeFiles.Count != expectedNames.Count ||
            actualRuntimeFiles.Keys.Any(name => name is null || !expectedNames.Contains(name)))
        {
            throw new InvalidDataException("自動起動マニフェストにない実行ファイルがあるか、必要なファイルが不足しています。");
        }

        foreach (var entry in manifest.Files)
        {
            var path = actualRuntimeFiles[entry.Name!];
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"再解析ポイントの実行ファイルは自動起動へ登録できません: {entry.Name}");
            }

            var actualHash = ComputeFileHash(path);
            var expectedHash = Convert.FromHexString(entry.Sha256!);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                throw new InvalidDataException($"自動起動用ファイルのSHA-256が一致しません: {entry.Name}");
            }
        }

        if (!allowUnrelatedSourceFiles)
        {
            if (Directory.EnumerateDirectories(fullDirectory, "*", SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidDataException("自動起動キャッシュに未承認のサブフォルダーがあります。");
            }

            var allowedNames = new HashSet<string>(expectedNames, StringComparer.OrdinalIgnoreCase)
            {
                ManifestFileName,
                SignatureFileName
            };
            var allFiles = Directory.EnumerateFiles(fullDirectory, "*", SearchOption.TopDirectoryOnly).ToArray();
            if (allFiles.Length != allowedNames.Count ||
                allFiles.Any(path => !allowedNames.Contains(Path.GetFileName(path))))
            {
                throw new InvalidDataException("自動起動キャッシュに未承認のファイルがあります。");
            }
        }

        return new VerifiedStartupRuntimeManifest(
            manifest.Files.Select(entry => entry.Name!).ToArray(),
            Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant());
    }

    private static void ValidateEntry(RuntimeManifestFile entry, ISet<string> names)
    {
        var name = entry.Name;
        if (string.IsNullOrWhiteSpace(name) ||
            !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal) ||
            Path.IsPathRooted(name) ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar) ||
            name.Contains(':') ||
            name is "." or ".." ||
            !RuntimeExtensions.Contains(Path.GetExtension(name)) ||
            !names.Add(name))
        {
            throw new InvalidDataException("自動起動マニフェストに不正または重複したファイル名があります。");
        }

        if (entry.Sha256 is null || entry.Sha256.Length != 64 ||
            !entry.Sha256.All(character => char.IsAsciiHexDigit(character)))
        {
            throw new InvalidDataException($"自動起動マニフェストのSHA-256が不正です: {name}");
        }
    }

    private static void VerifySignature(
        byte[] manifestBytes,
        byte[] signatureBytes,
        ReadOnlySpan<byte> expectedSignerCertificate)
    {
        var signedCms = new SignedCms(new ContentInfo(manifestBytes), detached: true);
        signedCms.Decode(signatureBytes);
        signedCms.CheckSignature(verifySignatureOnly: true);
        if (signedCms.SignerInfos.Count != 1)
        {
            throw new CryptographicException("自動起動マニフェストの署名者数が不正です。");
        }

        using var signerCertificate = signedCms.SignerInfos[0].Certificate
            ?? throw new CryptographicException("自動起動マニフェストに署名証明書が含まれていません。");
        if (!HasCodeSigningUsage(signerCertificate) ||
            !CryptographicOperations.FixedTimeEquals(signerCertificate.RawData, expectedSignerCertificate))
        {
            throw new CryptographicException("自動起動マニフェストの署名者が、実行中の罫彩と一致しません。");
        }
    }

    private static bool HasCodeSigningUsage(X509Certificate2 certificate) =>
        certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(extension => extension.EnhancedKeyUsages.Cast<Oid>())
            .Any(usage => string.Equals(usage.Value, "1.3.6.1.5.5.7.3.3", StringComparison.Ordinal));

    private static byte[] ReadBoundedFile(string path, int maximumBytes, string description)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException($"{description}が大きすぎます。");
        }

        var bytes = new byte[(int)stream.Length];
        var total = 0;
        while (total < bytes.Length)
        {
            var read = stream.Read(bytes, total, bytes.Length - total);
            if (read == 0)
            {
                Array.Resize(ref bytes, total);
                return bytes;
            }
            total += read;
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException($"{description}が読込中に上限を超えました。");
        }
        return bytes;
    }

    private static byte[] ComputeFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream);
    }

    private sealed class RuntimeManifest
    {
        public int Format { get; set; }

        public string? Product { get; set; }

        public string? Version { get; set; }

        public List<RuntimeManifestFile>? Files { get; set; }
    }

    private sealed class RuntimeManifestFile
    {
        public string? Name { get; set; }

        public string? Sha256 { get; set; }
    }
}

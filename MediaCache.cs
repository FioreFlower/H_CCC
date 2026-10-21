sealed class MediaCache
{
    private static readonly Regex SafeSegment = new("[^a-zA-Z0-9_.-]", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, string> ExtensionsByContentType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["image/avif"] = ".avif",
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["application/zip"] = ".zip",
        ["application/x-zip-compressed"] = ".zip"
    };

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public MediaCache(IHttpClientFactory httpClientFactory, IConfiguration configuration, IWebHostEnvironment environment)
    {
        _httpClient = httpClientFactory.CreateClient("MediaCache");
        RootPath = GetRootPath(configuration, environment);
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(Path.Combine(RootPath, "thumb"));
        Directory.CreateDirectory(Path.Combine(RootPath, "original"));
        Directory.CreateDirectory(Path.Combine(RootPath, "galleries"));
    }

    public string RootPath { get; }

    public static string GetRootPath(IConfiguration configuration, IWebHostEnvironment environment) =>
        Path.GetFullPath(configuration["Cache:MediaPath"] ?? Path.Combine(environment.ContentRootPath, "cache", "media"));

    public bool TryGetCachedPath(string kind, string id, out string path)
    {
        var safeKind = NormalizeKind(kind);
        var safeId = SafeSegment.Replace(id, "_");
        var directory = Path.Combine(RootPath, safeKind);
        path = Directory.Exists(directory)
            ? FindExistingFile(directory, safeId) ?? ""
            : "";
        return !string.IsNullOrWhiteSpace(path);
    }

    public bool TryGetCachedPublicUrl(string kind, string id, out string url)
    {
        if (TryGetCachedPath(kind, id, out var path))
        {
            url = ToPublicUrl(NormalizeKind(kind), path);
            return true;
        }

        url = "";
        return false;
    }

    public async Task<string?> CacheAsync(string kind, string id, string? remoteUrl, CancellationToken cancellationToken)
    {
        var filePath = await CachePathAsync(kind, id, remoteUrl, cancellationToken);
        return filePath is null ? null : ToPublicUrl(NormalizeKind(kind), filePath);
    }

    public async Task<string?> CachePathAsync(string kind, string id, string? remoteUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var safeKind = NormalizeKind(kind);
        var safeId = SafeSegment.Replace(id, "_");
        var extension = GetExtensionFromUrl(remoteUrl) ?? ".bin";
        var directory = Path.Combine(RootPath, safeKind);
        Directory.CreateDirectory(directory);
        var finalPath = Path.Combine(directory, safeId + extension);
        var existingPath = FindExistingFile(directory, safeId);
        if (existingPath is not null)
        {
            return existingPath;
        }

        var fileLock = _locks.GetOrAdd($"{safeKind}:{safeId}", _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            existingPath = FindExistingFile(directory, safeId);
            if (existingPath is not null)
            {
                return existingPath;
            }

            var downloaded = await DownloadAsync(remoteUrl, cancellationToken);
            extension = GetExtensionFromContentType(downloaded.ContentType) ?? GetExtensionFromUrl(remoteUrl) ?? ".bin";
            finalPath = Path.Combine(directory, safeId + extension);
            var tempPath = finalPath + ".tmp";

            await File.WriteAllBytesAsync(tempPath, downloaded.Bytes, cancellationToken);

            File.Move(tempPath, finalPath, overwrite: true);
            return finalPath;
        }
        finally
        {
            fileLock.Release();
        }
    }

    private static string NormalizeKind(string kind)
    {
        var parts = kind.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => SafeSegment.Replace(part, "_"))
            .Where(part => !string.IsNullOrWhiteSpace(part) && part is not "." and not "..")
            .ToArray();

        if (parts.Length == 1)
        {
            return parts[0] == "original" ? "original" : "thumb";
        }

        if (parts.Length == 3 &&
            parts[0] == "galleries" &&
            (parts[2] == "thumb" || parts[2] == "original"))
        {
            return Path.Combine(parts);
        }

        return "thumb";
    }

    private async Task<DownloadedMedia> DownloadAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        using var response = await HttpRetry.SendAsync(
            attemptToken => _httpClient.GetAsync(remoteUrl, HttpCompletionOption.ResponseHeadersRead, attemptToken),
            TimeSpan.FromSeconds(45),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return new DownloadedMedia(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            response.Content.Headers.ContentType?.MediaType);
    }

    private static string? FindExistingFile(string directory, string safeId)
    {
        return Directory.EnumerateFiles(directory, safeId + ".*")
            .FirstOrDefault(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetExtensionFromUrl(string remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 8 ? null : extension.ToLowerInvariant();
    }

    private static string? GetExtensionFromContentType(string? contentType) =>
        contentType is not null && ExtensionsByContentType.TryGetValue(contentType, out var extension)
            ? extension
            : null;

    private static string ToPublicUrl(string kind, string filePath) =>
        "/media-cache/" + string.Join('/',
            kind.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(Uri.EscapeDataString)
                .Append(Uri.EscapeDataString(Path.GetFileName(filePath))));
}

sealed record DownloadedMedia(byte[] Bytes, string? ContentType);

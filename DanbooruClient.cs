sealed class DanbooruClient
{
    private const string DefaultTags = "all";
    private const int DefaultLimit = 30;
    private const int MaxLimit = 100;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly MediaCache _mediaCache;
    private readonly PostMediaRegistry _mediaRegistry;
    private readonly int _mediaCacheConcurrency;

    public DanbooruClient(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        MediaCache mediaCache,
        PostMediaRegistry mediaRegistry,
        IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient("Danbooru");
        _cache = cache;
        _mediaCache = mediaCache;
        _mediaRegistry = mediaRegistry;
        _mediaCacheConcurrency = Math.Clamp(configuration.GetValue("Danbooru:MediaCacheConcurrency", 4), 1, 16);
    }

    public async Task<PostPage> GetPostsAsync(
        string? tags,
        int? page,
        int? limit,
        CancellationToken cancellationToken,
        bool cacheOriginals = false,
        string? galleryCacheId = null)
    {
        var normalizedTags = string.IsNullOrWhiteSpace(tags) ? DefaultTags : tags.Trim();
        var normalizedPage = Math.Max(page ?? 0, 0);
        var normalizedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var cacheKey = $"posts:{normalizedTags}:{normalizedPage}:{normalizedLimit}";

        if (_cache.TryGetValue(cacheKey, out PostPage? cached) && cached is not null)
        {
            if (!cacheOriginals)
            {
                return cached;
            }
        }

        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("tags", ToDanbooruTags(normalizedTags)),
            new("page", (normalizedPage + 1).ToString()),
            new("limit", normalizedLimit.ToString())
        };

        var requestUri = "posts.json" + QueryString.Create(parameters).ToUriComponent();
        using var document = await GetDanbooruJsonAsync(requestUri, cancellationToken);

        var remotePostCount = document.RootElement.GetArrayLength();
        var remotePosts = document.RootElement.EnumerateArray()
            .Select(NormalizePost)
            .Where(post => post.Id is not null && HasImageUrl(post))
            .ToArray();
        var posts = await CachePostMediaAsync(remotePosts, cancellationToken, cacheOriginals, galleryCacheId);

        var result = new PostPage(
            normalizedTags,
            normalizedPage,
            normalizedLimit,
            0,
            remotePostCount == normalizedLimit,
            posts);

        if (!cacheOriginals)
        {
            _cache.Set(cacheKey, result, CacheTtl);
        }
        return result;
    }

    public async Task<PostDto?> GetPostAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var normalizedId = id.Trim();
        var cacheKey = $"post:{normalizedId}";
        if (_cache.TryGetValue(cacheKey, out PostDto? cached) && cached is not null)
        {
            return cached;
        }

        using var document = await GetDanbooruJsonAsync($"posts/{Uri.EscapeDataString(normalizedId)}.json", cancellationToken);
        var post = NormalizePost(document.RootElement);
        if (post.Id is null || !HasImageUrl(post))
        {
            return null;
        }

        var cachedPosts = await CachePostMediaAsync(new[] { post }, cancellationToken);
        var result = cachedPosts.FirstOrDefault();
        if (result is not null)
        {
            _cache.Set(cacheKey, result, CacheTtl);
        }

        return result;
    }

    public async Task<IReadOnlyList<TagSuggestion>> GetTagSuggestionsAsync(string? query, int? limit, CancellationToken cancellationToken)
    {
        var normalizedQuery = (query ?? "").Trim();
        var normalizedLimit = Math.Clamp(limit ?? 10, 1, 20);
        if (normalizedQuery.Length < 2)
        {
            return Array.Empty<TagSuggestion>();
        }

        var cacheKey = $"tag-suggest:{normalizedQuery}:{normalizedLimit}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<TagSuggestion>? cached) && cached is not null)
        {
            return cached;
        }

        var parameters = new List<KeyValuePair<string, string?>>
        {
            new("search[query]", normalizedQuery),
            new("search[type]", "tag_query"),
            new("limit", normalizedLimit.ToString())
        };
        var requestUri = "autocomplete.json" + QueryString.Create(parameters).ToUriComponent();
        using var document = await GetDanbooruJsonAsync(requestUri, cancellationToken);

        var suggestions = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Select(ParseTagSuggestion)
                .Where(suggestion => !string.IsNullOrWhiteSpace(suggestion.Value))
                .Take(normalizedLimit)
                .ToArray()
            : Array.Empty<TagSuggestion>();

        _cache.Set(cacheKey, suggestions, TimeSpan.FromMinutes(5));
        return suggestions;
    }

    private async Task<JsonDocument> GetDanbooruJsonAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await HttpRetry.SendAsync(
            attemptToken => _httpClient.GetAsync(requestUri, attemptToken),
            TimeSpan.FromSeconds(15),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task<PostDto[]> CachePostMediaAsync(
        PostDto[] remotePosts,
        CancellationToken cancellationToken,
        bool cacheOriginals = false,
        string? galleryCacheId = null)
    {
        using var throttler = new SemaphoreSlim(_mediaCacheConcurrency);
        var tasks = remotePosts.Select(async post =>
        {
            await throttler.WaitAsync(cancellationToken);
            try
            {
            if (post.Id is null)
            {
                return post;
            }

            var isVideo = string.Equals(post.MediaType, "video", StringComparison.Ordinal);
            var isArchive = string.Equals(post.MediaType, "archive", StringComparison.Ordinal);
            var thumbnailSource = FirstImageUrl(post.SampleUrl, post.PreviewUrl, post.FileUrl);
            var thumbnailKey = isVideo || isArchive ? $"poster-{post.Id}" : $"lg-{post.Id}";
            var thumbnailKind = CacheKind(galleryCacheId, "thumb");
            var originalKind = CacheKind(galleryCacheId, "original");
            var thumbnailUrl = await TryCacheMediaAsync(thumbnailKind, thumbnailKey, thumbnailSource, cancellationToken);
            if (!string.IsNullOrWhiteSpace(post.FileUrl))
            {
                _mediaRegistry.RegisterOriginal(post.Id, post.FileUrl);
                if (cacheOriginals)
                {
                    await TryCacheMediaAsync(originalKind, post.Id, post.FileUrl, cancellationToken);
                }
            }

            var originalUrl = string.IsNullOrWhiteSpace(galleryCacheId)
                ? $"/api/media/original/{Uri.EscapeDataString(post.Id)}"
                : $"/api/media/galleries/{Uri.EscapeDataString(galleryCacheId)}/original/{Uri.EscapeDataString(post.Id)}";

            return post with
            {
                PreviewUrl = thumbnailUrl,
                SampleUrl = null,
                FileUrl = null,
                OriginalUrl = originalUrl
            };
            }
            finally
            {
                throttler.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<string?> TryCacheMediaAsync(string kind, string id, string? remoteUrl, CancellationToken cancellationToken)
    {
        try
        {
            return await _mediaCache.CacheAsync(kind, id, remoteUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or IOException)
        {
            // A single unavailable media file should not fail the whole API page.
            return null;
        }
    }

    private static string CacheKind(string? galleryCacheId, string mediaKind) =>
        string.IsNullOrWhiteSpace(galleryCacheId)
            ? mediaKind
            : $"galleries/{galleryCacheId}/{mediaKind}";

    private static PostDto NormalizePost(JsonElement post)
    {
        var id = GetString(post, "id");
        var previewUrl = NormalizeRemoteUrl(GetString(post, "preview_file_url"));
        var sampleUrl = NormalizeRemoteUrl(GetString(post, "large_file_url"));
        var fileUrl = NormalizeRemoteUrl(GetString(post, "file_url")) ?? sampleUrl ?? previewUrl;
        var mediaType = GetMediaType(GetString(post, "file_ext"), fileUrl);
        if (string.Equals(mediaType, "video", StringComparison.Ordinal) ||
            string.Equals(mediaType, "archive", StringComparison.Ordinal))
        {
            previewUrl = GetBestMediaAssetImageUrl(post) ?? previewUrl;
            sampleUrl = FirstImageUrl(sampleUrl, previewUrl);
        }

        return new PostDto(
            id,
            mediaType,
            GetString(post, "rating") ?? "unknown",
            GetString(post, "tag_string") ?? "",
            GetInt(post, "image_width") ?? 0,
            GetInt(post, "image_height") ?? 0,
            GetInt(post, "score") ?? 0,
            GetString(post, "created_at"),
            GetString(post, "source") ?? "",
            previewUrl,
            sampleUrl,
            fileUrl,
            null,
            id is null ? null : $"https://danbooru.donmai.us/posts/{id}",
            BuildTagGroups(post));
    }

    private static IReadOnlyList<TagGroup> BuildTagGroups(JsonElement post)
    {
        var groups = new[]
        {
            new TagGroup("artist", SplitTags(GetString(post, "tag_string_artist"))),
            new TagGroup("character", SplitTags(GetString(post, "tag_string_character"))),
            new TagGroup("copyright", SplitTags(GetString(post, "tag_string_copyright"))),
            new TagGroup("general", SplitTags(GetString(post, "tag_string_general"))),
            new TagGroup("meta", SplitTags(GetString(post, "tag_string_meta")))
        };

        return groups.Where(group => group.Tags.Count > 0).ToArray();
    }

    private static IReadOnlyList<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Array.Empty<string>()
            : tags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetMediaType(string? fileExtension, string? fileUrl)
    {
        var extension = fileExtension;
        if (string.IsNullOrWhiteSpace(extension) &&
            !string.IsNullOrWhiteSpace(fileUrl) &&
            Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            extension = Path.GetExtension(uri.AbsolutePath).TrimStart('.');
        }

        return string.Equals(extension, "webm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, "mp4", StringComparison.OrdinalIgnoreCase)
            ? "video"
            : string.Equals(extension, "zip", StringComparison.OrdinalIgnoreCase)
                ? "archive"
                : "image";
    }

    private static string? FirstImageUrl(params string?[] urls) =>
        urls.FirstOrDefault(IsImageUrl);

    private static string? GetBestMediaAssetImageUrl(JsonElement post)
    {
        if (!post.TryGetProperty("media_asset", out var mediaAsset) ||
            !mediaAsset.TryGetProperty("variants", out var variants) ||
            variants.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return variants.EnumerateArray()
            .Select(variant => new
            {
                Url = NormalizeRemoteUrl(GetString(variant, "url")),
                Width = GetInt(variant, "width") ?? ParseVariantSize(GetString(variant, "type")),
                Height = GetInt(variant, "height") ?? ParseVariantSize(GetString(variant, "type"))
            })
            .Where(variant => IsImageUrl(variant.Url))
            .OrderByDescending(variant => variant.Width * variant.Height)
            .ThenByDescending(variant => variant.Width)
            .Select(variant => variant.Url)
            .FirstOrDefault();
    }

    private static int ParseVariantSize(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return 0;
        }

        var match = Regex.Match(type, @"(\d+)");
        return match.Success && int.TryParse(match.Value, out var size) ? size : 0;
    }

    private static bool IsImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".avif" => true,
            _ => false
        };
    }

    private static TagSuggestion ParseTagSuggestion(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var stringValue = item.GetString() ?? "";
            return new TagSuggestion(stringValue, stringValue, null, null);
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return new TagSuggestion("", "", null, null);
        }

        var value = GetString(item, "value") ??
            GetString(item, "name") ??
            GetString(item, "antecedent_name") ??
            "";
        var label = StripHtml(GetString(item, "label")) ?? value;
        var category = GetString(item, "category_name") ?? GetString(item, "category");
        var postCount = GetInt(item, "post_count") ?? GetInt(item, "posts");

        return new TagSuggestion(value, label, category, postCount);
    }

    private static string? StripHtml(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return Regex.Replace(value, "<.*?>", "").Trim();
    }

    private static string ToDanbooruTags(string tags) =>
        string.Equals(tags, DefaultTags, StringComparison.OrdinalIgnoreCase) ? "" : tags;

    private static string? NormalizeRemoteUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + url;
        }

        if (url.StartsWith("/", StringComparison.Ordinal))
        {
            return "https://danbooru.donmai.us" + url;
        }

        return url;
    }

    private static bool HasImageUrl(PostDto post) =>
        !string.IsNullOrWhiteSpace(post.PreviewUrl) ||
        !string.IsNullOrWhiteSpace(post.SampleUrl) ||
        !string.IsNullOrWhiteSpace(post.FileUrl);

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int? GetInt(JsonElement element, string propertyName) =>
        TryGetInt(element, propertyName, out var value) ? value : null;

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            int.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}

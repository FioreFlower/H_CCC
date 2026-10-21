sealed class GalleryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _storePath;
    private readonly string _postStorePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, CachedPostFile> _postCache = new(StringComparer.OrdinalIgnoreCase);

    public GalleryStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _storePath = Path.GetFullPath(configuration["Gallery:StorePath"] ??
            Path.Combine(environment.ContentRootPath, "data", "galleries.json"));
        var dataPath = Path.GetDirectoryName(_storePath)!;
        _postStorePath = Path.GetFullPath(configuration["Gallery:PostStorePath"] ??
            Path.Combine(dataPath, "gallery-posts"));
        Directory.CreateDirectory(dataPath);
        Directory.CreateDirectory(_postStorePath);
    }

    public async Task<IReadOnlyList<GalleryDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await ReadStateAsync(cancellationToken)).Items
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Title)
                .ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GalleryDto?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await ReadStateAsync(cancellationToken)).Items
                .FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GalleryDto> UpsertAsync(string tags, string? title, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            var normalizedTags = AppHelpers.NormalizeGalleryTags(tags);
            var now = DateTimeOffset.UtcNow;
            var displayTitle = string.IsNullOrWhiteSpace(title) ? normalizedTags : title.Trim();
            var existing = state.Items.FirstOrDefault(item =>
                string.Equals(item.Tags, normalizedTags, StringComparison.OrdinalIgnoreCase));

            GalleryDto next;
            if (existing is null)
            {
                next = new GalleryDto(
                    Guid.NewGuid().ToString("n"),
                    displayTitle,
                    normalizedTags,
                    now,
                    now,
                    null,
                    null,
                    null,
                    null,
                    0,
                    0,
                    false,
                    null);
                state.Items.Add(next);
            }
            else
            {
                next = existing with
                {
                    Title = displayTitle,
                    UpdatedAt = now
                };
                state.Items[state.Items.IndexOf(existing)] = next;
            }

            await WriteStateAsync(state, cancellationToken);
            return next;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            var removed = state.Items.RemoveAll(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                await WriteStateAsync(state, cancellationToken);
                var postPath = GetPostStoreFile(id);
                if (File.Exists(postPath))
                {
                    File.Delete(postPath);
                }
                _postCache.Remove(id);
            }

            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PostPage> GetCachedPostsAsync(GalleryDto gallery, int? page, int? limit, CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page ?? 0, 0);
        var normalizedLimit = Math.Clamp(limit ?? 40, 1, 100);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var posts = await ReadCachedPostsAsync(gallery.Id, cancellationToken);
            var pagePosts = posts
                .Skip(normalizedPage * normalizedLimit)
                .Take(normalizedLimit)
                .ToArray();

            return new PostPage(
                gallery.Tags,
                normalizedPage,
                normalizedLimit,
                posts.Count,
                posts.Count > (normalizedPage + 1) * normalizedLimit,
                pagePosts);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> UpsertCachedPostsAsync(string galleryId, IReadOnlyList<PostDto> posts, CancellationToken cancellationToken)
    {
        if (posts.Count == 0)
        {
            return await GetCachedPostCountAsync(galleryId, cancellationToken);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var existing = await ReadCachedPostsAsync(galleryId, cancellationToken);
            var byId = existing
                .Where(post => !string.IsNullOrWhiteSpace(post.Id))
                .ToDictionary(post => post.Id!, StringComparer.Ordinal);

            foreach (var post in posts.Where(post => !string.IsNullOrWhiteSpace(post.Id)))
            {
                byId[post.Id!] = post;
            }

            var next = byId.Values
                .OrderByDescending(post => long.TryParse(post.Id, out var id) ? id : 0)
                .ToArray();

            if (HasSameCachedPosts(existing, next))
            {
                return existing.Count;
            }

            await WriteCachedPostsAsync(galleryId, next, cancellationToken);
            return next.Length;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetCachedPostCountAsync(string galleryId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await ReadCachedPostsAsync(galleryId, cancellationToken)).Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkCacheProgressAsync(
        string id,
        string? latestPostId,
        string? firstPageTailPostId,
        string? catchupUntilPostId,
        int nextPageToCache,
        int cachedPostCount,
        bool isFullyCached,
        string? error,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadStateAsync(cancellationToken);
            var existing = state.Items.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return;
            }

            var next = existing with
            {
                LastCheckedAt = DateTimeOffset.UtcNow,
                LatestPostId = latestPostId ?? existing.LatestPostId,
                FirstPageTailPostId = firstPageTailPostId ?? existing.FirstPageTailPostId,
                CatchupUntilPostId = catchupUntilPostId,
                NextPageToCache = Math.Max(nextPageToCache, 0),
                CachedPostCount = cachedPostCount,
                IsFullyCached = isFullyCached,
                LastError = error,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            state.Items[state.Items.IndexOf(existing)] = next;
            await WriteStateAsync(state, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<GalleryState> ReadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storePath))
        {
            return new GalleryState();
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<GalleryState>(stream, JsonOptions, cancellationToken) ??
            new GalleryState();
    }

    private async Task WriteStateAsync(GalleryState state, CancellationToken cancellationToken)
    {
        var tempPath = _storePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _storePath, overwrite: true);
    }

    private string GetPostStoreFile(string galleryId) =>
        Path.Combine(_postStorePath, SafeFileName.Replace(galleryId, "_") + ".json");

    private async Task<List<PostDto>> ReadCachedPostsAsync(string galleryId, CancellationToken cancellationToken)
    {
        var path = GetPostStoreFile(galleryId);
        if (!File.Exists(path))
        {
            _postCache.Remove(galleryId);
            return new List<PostDto>();
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(path);
        if (_postCache.TryGetValue(galleryId, out var cached) && cached.LastWriteUtc == lastWriteUtc)
        {
            return cached.Posts;
        }

        await using var stream = File.OpenRead(path);
        var posts = await JsonSerializer.DeserializeAsync<List<PostDto>>(stream, JsonOptions, cancellationToken) ??
            new List<PostDto>();
        _postCache[galleryId] = new CachedPostFile(lastWriteUtc, posts);
        return posts;
    }

    private async Task WriteCachedPostsAsync(string galleryId, IReadOnlyList<PostDto> posts, CancellationToken cancellationToken)
    {
        var path = GetPostStoreFile(galleryId);
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, posts, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
        _postCache[galleryId] = new CachedPostFile(File.GetLastWriteTimeUtc(path), posts.ToList());
    }

    private static bool HasSameCachedPosts(IReadOnlyList<PostDto> existing, IReadOnlyList<PostDto> next)
    {
        if (existing.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < existing.Count; index++)
        {
            if (!string.Equals(existing[index].Id, next[index].Id, StringComparison.Ordinal) ||
                !string.Equals(existing[index].PreviewUrl, next[index].PreviewUrl, StringComparison.Ordinal) ||
                !string.Equals(existing[index].OriginalUrl, next[index].OriginalUrl, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly Regex SafeFileName = new("[^a-zA-Z0-9_.-]", RegexOptions.Compiled);

    private sealed record CachedPostFile(DateTime LastWriteUtc, List<PostDto> Posts);
}

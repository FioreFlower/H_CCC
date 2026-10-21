sealed class GalleryRefreshService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GalleryRefreshService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _activeInterval;
    private readonly TimeSpan _pageDelay;
    private readonly int _limit;
    private readonly int _pagesPerCycle;
    private readonly int _maxCatchupPages;
    private readonly bool _cacheOriginals;

    public GalleryRefreshService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<GalleryRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(Math.Clamp(configuration.GetValue("Gallery:RefreshMinutes", 15), 1, 1440));
        _activeInterval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Gallery:ActiveCacheSeconds", 5), 1, 3600));
        _pageDelay = TimeSpan.FromMilliseconds(Math.Clamp(configuration.GetValue("Gallery:PageDelayMs", 250), 0, 10000));
        _limit = Math.Clamp(configuration.GetValue("Gallery:PreloadLimit", 40), 1, 100);
        _pagesPerCycle = Math.Clamp(configuration.GetValue("Gallery:PagesPerCycle", 3), 1, 100);
        _maxCatchupPages = Math.Clamp(configuration.GetValue("Gallery:MaxCatchupPages", 25), 1, 1000);
        _cacheOriginals = configuration.GetValue("Gallery:CacheOriginals", false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var hasRemainingWork = await RefreshOnceAsync(stoppingToken);
            await Task.Delay(hasRemainingWork ? _activeInterval : _interval, stoppingToken);
        }
    }

    private async Task<bool> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var galleries = scope.ServiceProvider.GetRequiredService<GalleryStore>();
        var danbooru = scope.ServiceProvider.GetRequiredService<DanbooruClient>();
        var items = await galleries.GetAllAsync(cancellationToken);
        var hasRemainingWork = false;

        foreach (var gallery in items)
        {
            try
            {
                if (string.Equals(gallery.Tags, "all", StringComparison.OrdinalIgnoreCase))
                {
                    await galleries.MarkCacheProgressAsync(
                        gallery.Id,
                        gallery.LatestPostId,
                        gallery.FirstPageTailPostId,
                        null,
                        0,
                        gallery.CachedPostCount,
                        true,
                        "The global all feed is skipped by gallery auto-cache.",
                        cancellationToken);
                    continue;
                }

                var latestPostId = gallery.LatestPostId;
                var firstPageTailPostId = gallery.FirstPageTailPostId;
                var catchupUntilPostId = gallery.CatchupUntilPostId;
                var nextPage = gallery.NextPageToCache;
                var cachedPostCount = gallery.CachedPostCount;
                var isFullyCached = gallery.IsFullyCached;

                if (!string.IsNullOrWhiteSpace(catchupUntilPostId))
                {
                    var catchup = await CacheUntilBoundaryAsync(
                        galleries,
                        danbooru,
                        gallery.Id,
                        gallery.Tags,
                        nextPage,
                        catchupUntilPostId,
                        cachedPostCount,
                        _pagesPerCycle,
                        cancellationToken);
                    cachedPostCount = catchup.CachedPostCount;
                    nextPage = catchup.NextPage;
                    isFullyCached = catchup.FoundBoundary || catchup.ReachedEnd;
                    catchupUntilPostId = isFullyCached ? null : catchupUntilPostId;
                    if (isFullyCached)
                    {
                        nextPage = 0;
                    }
                }
                else if (isFullyCached)
                {
                    var latestPage = await danbooru.GetPostsAsync(gallery.Tags, 0, _limit, cancellationToken, cacheOriginals: _cacheOriginals, galleryCacheId: gallery.Id);
                    cachedPostCount = await galleries.UpsertCachedPostsAsync(gallery.Id, latestPage.Posts, cancellationToken);
                    var currentLatestPostId = latestPage.Posts.FirstOrDefault()?.Id;
                    var currentFirstPageTailPostId = latestPage.Posts.LastOrDefault()?.Id;
                    if (!string.IsNullOrWhiteSpace(currentLatestPostId) &&
                        !string.Equals(currentLatestPostId, gallery.LatestPostId, StringComparison.Ordinal))
                    {
                        latestPostId = currentLatestPostId;
                        firstPageTailPostId = currentFirstPageTailPostId;
                        var boundaryPostId = gallery.FirstPageTailPostId;
                        _ = CountUntilBoundary(latestPage.Posts, boundaryPostId, out var foundBoundaryOnFirstPage);

                        if (string.IsNullOrWhiteSpace(boundaryPostId) || foundBoundaryOnFirstPage || !latestPage.HasMore)
                        {
                            nextPage = 0;
                            catchupUntilPostId = null;
                            isFullyCached = true;
                        }
                        else
                        {
                            var catchup = await CacheUntilBoundaryAsync(
                                galleries,
                                danbooru,
                                gallery.Id,
                                gallery.Tags,
                                1,
                                boundaryPostId,
                                cachedPostCount,
                                Math.Max(_pagesPerCycle - 1, 0),
                                cancellationToken);
                            cachedPostCount = catchup.CachedPostCount;
                            nextPage = catchup.NextPage;
                            isFullyCached = catchup.FoundBoundary || catchup.ReachedEnd;
                            catchupUntilPostId = isFullyCached ? null : boundaryPostId;
                            if (isFullyCached)
                            {
                                nextPage = 0;
                            }
                        }
                    }
                    else
                    {
                        await galleries.MarkCacheProgressAsync(
                            gallery.Id,
                            currentLatestPostId,
                            currentFirstPageTailPostId,
                            null,
                            nextPage,
                            cachedPostCount,
                            true,
                            null,
                            cancellationToken);
                        continue;
                    }
                }

                for (var i = 0; i < _pagesPerCycle && !isFullyCached; i++)
                {
                    var page = await danbooru.GetPostsAsync(gallery.Tags, nextPage, _limit, cancellationToken, cacheOriginals: _cacheOriginals, galleryCacheId: gallery.Id);
                    cachedPostCount = await galleries.UpsertCachedPostsAsync(gallery.Id, page.Posts, cancellationToken);
                    latestPostId ??= page.Posts.FirstOrDefault()?.Id;
                    if (nextPage == 0)
                    {
                        firstPageTailPostId = page.Posts.LastOrDefault()?.Id;
                    }
                    nextPage += 1;
                    isFullyCached = !page.HasMore;
                    if (isFullyCached)
                    {
                        nextPage = 0;
                    }

                    if (_pageDelay > TimeSpan.Zero && i < _pagesPerCycle - 1 && !isFullyCached)
                    {
                        await Task.Delay(_pageDelay, cancellationToken);
                    }
                }

                await galleries.MarkCacheProgressAsync(
                    gallery.Id,
                    latestPostId,
                    firstPageTailPostId,
                    catchupUntilPostId,
                    nextPage,
                    cachedPostCount,
                    isFullyCached,
                    null,
                    cancellationToken);
                if (!isFullyCached || !string.IsNullOrWhiteSpace(catchupUntilPostId))
                {
                    hasRemainingWork = true;
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                _logger.LogWarning(ex, "Gallery refresh failed for {Tags}", gallery.Tags);
                await galleries.MarkCacheProgressAsync(
                    gallery.Id,
                    null,
                    gallery.FirstPageTailPostId,
                    gallery.CatchupUntilPostId,
                    gallery.NextPageToCache,
                    gallery.CachedPostCount,
                    gallery.IsFullyCached,
                    AppHelpers.FormatExceptionMessage(ex),
                    cancellationToken);
                hasRemainingWork = true;
            }
        }

        return hasRemainingWork;
    }

    private async Task<GalleryCatchupResult> CacheUntilBoundaryAsync(
        GalleryStore galleries,
        DanbooruClient danbooru,
        string galleryId,
        string tags,
        int startPage,
        string boundaryPostId,
        int cachedPostCount,
        int maxPages,
        CancellationToken cancellationToken)
    {
        var nextPage = Math.Max(startPage, 0);
        var pagesToRead = Math.Min(Math.Max(maxPages, 0), _maxCatchupPages);
        for (var i = 0; i < pagesToRead; i++)
        {
            var page = await danbooru.GetPostsAsync(tags, nextPage, _limit, cancellationToken, cacheOriginals: _cacheOriginals, galleryCacheId: galleryId);
            cachedPostCount = await galleries.UpsertCachedPostsAsync(galleryId, page.Posts, cancellationToken);
            _ = CountUntilBoundary(page.Posts, boundaryPostId, out var foundBoundary);
            nextPage += 1;

            if (foundBoundary)
            {
                return new GalleryCatchupResult(nextPage, cachedPostCount, true, false);
            }

            if (!page.HasMore)
            {
                return new GalleryCatchupResult(0, cachedPostCount, false, true);
            }

            if (_pageDelay > TimeSpan.Zero && i < pagesToRead - 1)
            {
                await Task.Delay(_pageDelay, cancellationToken);
            }
        }

        return new GalleryCatchupResult(nextPage, cachedPostCount, false, false);
    }

    private static int CountUntilBoundary(IReadOnlyList<PostDto> posts, string? boundaryPostId, out bool foundBoundary)
    {
        foundBoundary = false;
        if (string.IsNullOrWhiteSpace(boundaryPostId))
        {
            return posts.Count;
        }

        for (var index = 0; index < posts.Count; index++)
        {
            if (string.Equals(posts[index].Id, boundaryPostId, StringComparison.Ordinal))
            {
                foundBoundary = true;
                return index;
            }
        }

        return posts.Count;
    }
}

sealed record GalleryCatchupResult(
    int NextPage,
    int CachedPostCount,
    bool FoundBoundary,
    bool ReachedEnd);

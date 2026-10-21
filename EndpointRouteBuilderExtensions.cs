static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMegadereApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/posts", async (
            string? tags,
            int? page,
            int? limit,
            DanbooruClient danbooru,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await danbooru.GetPostsAsync(tags, page, limit, cancellationToken);
                return Results.Ok(result);
            }
            catch (TaskCanceledException)
            {
                return Results.Problem("Danbooru request timed out.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
            catch (JsonException)
            {
                return Results.Problem("Danbooru returned an unexpected response.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/tags/autocomplete", async (
            string? query,
            int? limit,
            DanbooruClient danbooru,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var suggestions = await danbooru.GetTagSuggestionsAsync(query, limit, cancellationToken);
                return Results.Ok(suggestions);
            }
            catch (TaskCanceledException)
            {
                return Results.Problem("Danbooru autocomplete request timed out.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
            catch (JsonException)
            {
                return Results.Problem("Danbooru returned an unexpected autocomplete response.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/posts/{id}", async (
            string id,
            DanbooruClient danbooru,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var post = await danbooru.GetPostAsync(id, cancellationToken);
                return post is null
                    ? Results.NotFound(new { error = "Post not found." })
                    : Results.Ok(post);
            }
            catch (TaskCanceledException)
            {
                return Results.Problem("Danbooru request timed out.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (HttpRequestException ex)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
            catch (JsonException)
            {
                return Results.Problem("Danbooru returned an unexpected response.", statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
            {
                return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
            }
        });

        app.MapGet("/api/galleries", async (
            GalleryStore galleries,
            CancellationToken cancellationToken) =>
        {
            var items = await galleries.GetAllAsync(cancellationToken);
            return Results.Ok(items);
        });

        app.MapPost("/api/galleries", async (
            GalleryCreateRequest request,
            GalleryStore galleries,
            CancellationToken cancellationToken) =>
        {
            var tags = AppHelpers.NormalizeGalleryTags(request.Tags);
            if (string.IsNullOrWhiteSpace(tags))
            {
                return Results.BadRequest(new { error = "Gallery tags are required." });
            }
            if (string.Equals(tags, "all", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "The global all feed cannot be added as an auto-cached gallery." });
            }

            var item = await galleries.UpsertAsync(tags, request.Title, cancellationToken);
            return Results.Ok(item);
        });

        app.MapDelete("/api/galleries/{id}", async (
            string id,
            GalleryStore galleries,
            CancellationToken cancellationToken) =>
        {
            var removed = await galleries.DeleteAsync(id, cancellationToken);
            return removed ? Results.NoContent() : Results.NotFound(new { error = "Gallery not found." });
        });

        app.MapGet("/api/galleries/{id}", async (
            string id,
            GalleryStore galleries,
            CancellationToken cancellationToken) =>
        {
            var gallery = await galleries.GetAsync(id, cancellationToken);
            return gallery is null
                ? Results.NotFound(new { error = "Gallery not found." })
                : Results.Ok(gallery);
        });

        app.MapGet("/api/galleries/{id}/posts", async (
            string id,
            int? page,
            int? limit,
            GalleryStore galleries,
            CancellationToken cancellationToken) =>
        {
            var gallery = await galleries.GetAsync(id, cancellationToken);
            if (gallery is null)
            {
                return Results.NotFound(new { error = "Gallery not found." });
            }

            var result = await galleries.GetCachedPostsAsync(gallery, page, limit, cancellationToken);
            return Results.Ok(result);
        });

        app.MapGet("/api/media/original/{id}", async (
            string id,
            PostMediaRegistry registry,
            MediaCache cache,
            CancellationToken cancellationToken) =>
        {
            return await CacheOriginalAsync("original", id, registry, cache, cancellationToken);
        });

        app.MapGet("/api/media/galleries/{galleryId}/original/{id}", async (
            string galleryId,
            string id,
            PostMediaRegistry registry,
            MediaCache cache,
            CancellationToken cancellationToken) =>
        {
            return await CacheOriginalAsync($"galleries/{galleryId}/original", id, registry, cache, cancellationToken);
        });

        app.MapGet("/api/media/original/{id}/download", async (
            string id,
            PostMediaRegistry registry,
            MediaCache cache,
            CancellationToken cancellationToken) =>
        {
            return await DownloadOriginalAsync("original", id, registry, cache, cancellationToken);
        });

        app.MapGet("/api/media/galleries/{galleryId}/original/{id}/download", async (
            string galleryId,
            string id,
            PostMediaRegistry registry,
            MediaCache cache,
            CancellationToken cancellationToken) =>
        {
            return await DownloadOriginalAsync($"galleries/{galleryId}/original", id, registry, cache, cancellationToken);
        });

        return app;
    }

    private static async Task<IResult> CacheOriginalAsync(
        string cacheKind,
        string id,
        PostMediaRegistry registry,
        MediaCache cache,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGetOriginalUrl(id, out var originalUrl))
        {
            return cache.TryGetCachedPublicUrl(cacheKind, id, out var cachedUrl)
                ? Results.Redirect(cachedUrl, permanent: false)
                : Results.NotFound(new { error = "Original file URL is not registered. Load the post list first." });
        }

        try
        {
            var cachedUrl = await cache.CacheAsync(cacheKind, id, originalUrl, cancellationToken);
            return cachedUrl is null
                ? Results.NotFound(new { error = "Original file URL is empty." })
                : Results.Redirect(cachedUrl, permanent: false);
        }
        catch (TaskCanceledException)
        {
            return Results.Problem("Original file download timed out.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> DownloadOriginalAsync(
        string cacheKind,
        string id,
        PostMediaRegistry registry,
        MediaCache cache,
        CancellationToken cancellationToken)
    {
        if (!registry.TryGetOriginalUrl(id, out var originalUrl))
        {
            return cache.TryGetCachedPath(cacheKind, id, out var existingPath)
                ? DownloadCachedOriginal(existingPath, id)
                : Results.NotFound(new { error = "Original file URL is not registered. Load the post list first." });
        }

        try
        {
            var cachedPath = await cache.CachePathAsync(cacheKind, id, originalUrl, cancellationToken);
            return cachedPath is null
                ? Results.NotFound(new { error = "Original file URL is empty." })
                : DownloadCachedOriginal(cachedPath, id);
        }
        catch (TaskCanceledException)
        {
            return Results.Problem("Original file download timed out.", statusCode: StatusCodes.Status502BadGateway);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(AppHelpers.FormatExceptionMessage(ex), statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult DownloadCachedOriginal(string cachedPath, string id)
    {
        var extension = Path.GetExtension(cachedPath);
        var downloadName = $"danbooru-{id}{extension}";
        return Results.File(cachedPath, fileDownloadName: downloadName, enableRangeProcessing: true);
    }
}

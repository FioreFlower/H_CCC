sealed record PostPage(
    string Tags,
    int Page,
    int Limit,
    int Count,
    bool HasMore,
    IReadOnlyList<PostDto> Posts);

sealed record TagSuggestion(
    string Value,
    string Label,
    string? Category,
    int? PostCount);

sealed record TagGroup(
    string Category,
    IReadOnlyList<string> Tags);

sealed class GalleryState
{
    public List<GalleryDto> Items { get; set; } = new();
}

sealed record GalleryCreateRequest(
    string? Tags,
    string? Title);

sealed record GalleryDto(
    string Id,
    string Title,
    string Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastCheckedAt,
    string? LatestPostId,
    string? FirstPageTailPostId,
    string? CatchupUntilPostId,
    int CachedPostCount,
    int NextPageToCache,
    bool IsFullyCached,
    string? LastError);

sealed record PostDto(
    string? Id,
    string MediaType,
    string Rating,
    string Tags,
    int Width,
    int Height,
    int Score,
    string? CreatedAt,
    string Source,
    string? PreviewUrl,
    string? SampleUrl,
    string? FileUrl,
    string? OriginalUrl,
    string? PostUrl,
    IReadOnlyList<TagGroup> TagGroups);

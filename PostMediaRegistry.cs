sealed class PostMediaRegistry
{
    private readonly ConcurrentDictionary<string, string> _originalUrls = new();

    public void RegisterOriginal(string id, string originalUrl) =>
        _originalUrls[id] = originalUrl;

    public bool TryGetOriginalUrl(string id, out string originalUrl) =>
        _originalUrls.TryGetValue(id, out originalUrl!);
}

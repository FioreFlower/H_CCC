static class AppHelpers
{
    public static string FormatExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    public static string NormalizeGalleryTags(string? tags)
    {
        var normalized = string.Join(' ', (tags ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "all" : normalized;
    }
}

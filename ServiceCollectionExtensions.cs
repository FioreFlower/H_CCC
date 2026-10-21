static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMegadereServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();
        services.AddSingleton<DanbooruClient>();
        services.AddSingleton<MediaCache>();
        services.AddSingleton<PostMediaRegistry>();
        services.AddSingleton<GalleryStore>();
        services.AddHostedService<GalleryRefreshService>();

        services.AddHttpClient("Danbooru", client =>
        {
            client.BaseAddress = new Uri(configuration["Danbooru:BaseUrl"] ?? "https://danbooru.donmai.us/");
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

            var userAgent = configuration["Danbooru:UserAgent"];
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                string.IsNullOrWhiteSpace(userAgent)
                    ? "megadere/0.1 (+local development)"
                    : userAgent);

            var username = configuration["Danbooru:Username"] ?? configuration["DANBOORU_USERNAME"];
            var apiKey = configuration["Danbooru:ApiKey"] ?? configuration["DANBOORU_API_KEY"];
            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(apiKey))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{apiKey}"));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            var cookie = configuration["Danbooru:Cookie"] ?? configuration["DANBOORU_COOKIE"];
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }
        })
            .ConfigurePrimaryHttpMessageHandler(() => CreateHttpClientHandler(configuration));

        services.AddHttpClient("MediaCache", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("megadere/0.1 (+local development)");
        })
            .ConfigurePrimaryHttpMessageHandler(() => CreateHttpClientHandler(configuration));

        return services;
    }

    private static HttpClientHandler CreateHttpClientHandler(IConfiguration configuration)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        };
        var proxyUrl = configuration["Danbooru:ProxyUrl"] ??
            configuration["HTTPS_PROXY"] ??
            configuration["HTTP_PROXY"];
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl);
            handler.UseProxy = true;
        }

        return handler;
    }
}

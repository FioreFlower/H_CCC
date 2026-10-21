static class ApplicationBuilderExtensions
{
    public static WebApplication UseMegadereStaticFiles(this WebApplication app)
    {
        var mediaRootPath = MediaCache.GetRootPath(app.Configuration, app.Environment);
        Directory.CreateDirectory(mediaRootPath);

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(mediaRootPath),
            RequestPath = "/media-cache",
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public,max-age=604800,immutable";
            }
        });

        return app;
    }
}

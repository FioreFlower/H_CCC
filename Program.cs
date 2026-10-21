var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.AddMegadereServices(builder.Configuration);

var app = builder.Build();

app.UseMegadereStaticFiles();
app.MapMegadereApi();
app.MapFallbackToFile("index.html");

app.Run();

// ToolEngine.Ui — standalone demo UI host
// Serves wwwroot/index.html and exposes /config so the browser knows the API URL.
// Run alongside ToolEngine.Api: UI on :5001, API on :5000.
//
// To point at a different API instance:
//   set ApiBaseUrl=https://my-api.example.com in appsettings or env vars

var builder = WebApplication.CreateBuilder(args);
var app     = builder.Build();

// Serve index.html at /
app.UseDefaultFiles();
app.UseStaticFiles();

// Config endpoint — browser fetches /config on load to discover the API URL.
// Keeps the URL out of the HTML so you can deploy UI and API independently.
app.MapGet("/config", (IConfiguration config) =>
    Results.Ok(new { apiBaseUrl = config["ApiBaseUrl"] ?? "http://localhost:5000" }))
   .AllowAnonymous();

app.Run();

// ToolEngine.Ui — standalone demo UI host — also runs on AWS Lambda (BUFFERED mode)
// Serves wwwroot/index.html and exposes /config so the browser knows the API URL.
// Run alongside ToolEngine.Api: UI on :5001, API on :5000.
//
// To point at a different API instance:
//   set ApiBaseUrl=https://my-api.example.com in appsettings or env vars

var builder = WebApplication.CreateBuilder(args);

// ── Lambda hosting for UI ─────────────────────────────────────────────────────
// UI uses BUFFERED mode — serves static files + /config endpoint, no streaming needed.
// No code changes required when switching between local Kestrel and Lambda.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME")))
    builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();

// ── Security headers ──────────────────────────────────────────────────────────
// Google Identity Services delivers the credential via window.postMessage from
// accounts.google.com — a cross-origin frame/popup.  Any COOP value stricter than
// "unsafe-none" causes Chrome to block that postMessage channel and log:
//   "Cross-Origin-Opener-Policy policy would block the window.postMessage call."
// Setting COOP to "unsafe-none" disables the opener isolation policy, which is
// acceptable for this internal SPA where SharedArrayBuffer is not required.
app.Use(async (ctx, next) =>
{
    // COOP/COEP — must stay "unsafe-none" so Google Identity Services postMessage works
    ctx.Response.Headers["Cross-Origin-Opener-Policy"]   = "unsafe-none";
    ctx.Response.Headers["Cross-Origin-Embedder-Policy"] = "unsafe-none";

    // M7: additional OWASP security headers
    ctx.Response.Headers["X-Frame-Options"]           = "DENY";
    ctx.Response.Headers["X-Content-Type-Options"]    = "nosniff";
    ctx.Response.Headers["Referrer-Policy"]           = "strict-origin-when-cross-origin";

    // ── Content Security Policy ───────────────────────────────────────────────
    // Development adds:
    //   connect-src  localhost:* / ws://localhost:* / wss://localhost:*
    //                → BrowserLink (http://localhost:56554) + ASP.NET hot-reload (wss://localhost:44355)
    //   style-src    accounts.google.com  → Google GSI stylesheet
    //   frame-src    accounts.google.com  → Google sign-in popup frame
    // Production keeps the strict policy with only the Lambda API origin.
    string csp;
    if (app.Environment.IsDevelopment())
    {
        csp =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' accounts.google.com; " +
            "style-src 'self' 'unsafe-inline' fonts.googleapis.com accounts.google.com; " +
            "font-src fonts.gstatic.com; " +
            "img-src 'self' data: assets.onebcg.com; " +
            "frame-src accounts.google.com; " +
            "connect-src 'self' http://localhost:* ws://localhost:* wss://localhost:* " +
                        "accounts.google.com";
    }
    else
    {
        csp =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' accounts.google.com; " +
            "style-src 'self' 'unsafe-inline' fonts.googleapis.com accounts.google.com; " +
            "font-src fonts.gstatic.com; " +
            "img-src 'self' data: assets.onebcg.com; " +
            "frame-src accounts.google.com; " +
            "connect-src 'self' https://h708rohph9.execute-api.ap-south-1.amazonaws.com accounts.google.com";
    }

    ctx.Response.Headers["Content-Security-Policy"] = csp;
    await next();
});

// Serve index.html at /
app.UseDefaultFiles();
app.UseStaticFiles();

// Config endpoint — browser fetches /config on load to discover the API URL and auth settings.
// Keeps deployment-specific values out of the HTML — UI and API can be deployed independently.
// googleClientId is not a secret (used in browser-side JS); allowedDomain enforced server-side too.
app.MapGet("/config", (IConfiguration config) =>
    Results.Ok(new
    {
        apiBaseUrl     = config["ApiBaseUrl"]           ?? "http://localhost:5000",
        googleClientId = config["Auth:Google:ClientId"] ?? "",
        allowedDomain  = config["Auth:AllowedDomain"]   ?? "onebcg.com",
    }))
   .AllowAnonymous();

app.Run();

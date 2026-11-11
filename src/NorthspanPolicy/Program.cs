using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using NorthspanPolicy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AppStore>();
builder.Services.AddHttpClient<IAiClient, AnthropicClient>(c => c.Timeout = TimeSpan.FromSeconds(90));
builder.Services.AddTransient<PolicyChecker>();

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = TextExtractor.MaxUploadBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = TextExtractor.MaxUploadBytes + 64 * 1024);

// The app runs behind Nginx on the same server, so trust its forwarded client IP.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("check", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1) }));
    o.AddPolicy("waitlist", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1) }));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; form-action 'self'";
    await next();
});
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok("ok"));

// What the page needs to know about checkout. During the soft launch checkout is off.
app.MapGet("/api/config", (IConfiguration config) => Results.Ok(new
{
    checkoutEnabled = config.GetValue("Checkout:Enabled", false),
}));

// Free policy check. The upload is read in memory, checked, and discarded; it is never saved.
app.MapPost("/api/check", async (HttpRequest request, PolicyChecker checker, AppStore store,
    IConfiguration config, ILogger<Program> log, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Upload a file or paste your policy text." });

    var form = await request.ReadFormAsync(ct);
    string text;
    try
    {
        var file = form.Files.GetFile("file");
        if (file is { Length: > 0 })
        {
            if (file.Length > TextExtractor.MaxUploadBytes)
                return Results.BadRequest(new { error = "That file is larger than 2 MB. Paste the text instead." });
            await using var s = file.OpenReadStream();
            text = await TextExtractor.ExtractAsync(s, file.FileName, ct);
        }
        else
        {
            text = TextExtractor.Clean(form["text"].ToString());
        }
    }
    catch (UnsupportedFileException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    if (text.Length < 200)
        return Results.BadRequest(new { error = "That looks too short to be a policy. Paste or upload the full text." });

    if (!store.TryRecordCheck(config.GetValue("Checks:DailyLimit", 200)))
        return Results.Json(new { error = "We've reached today's limit for free checks. Please try again tomorrow." },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    try
    {
        var result = await checker.CheckAsync(text, ct);
        return Results.Ok(PolicyChecker.ToPreview(result));
    }
    catch (CheckFailedException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        log.LogWarning("Policy check failed: {Type}", ex.GetType().Name);
        return Results.Json(new { error = "The check took too long. Please try again in a minute." },
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
}).RequireRateLimiting("check");

// "Coming soon, notify me" sign-ups during the soft launch.
app.MapPost("/api/waitlist", (WaitlistRequest req, AppStore store) =>
{
    if (!AppStore.IsValidEmail(req.Email))
        return Results.BadRequest(new { error = "Enter a valid email address." });
    if (req.Interest is not ("create" or "report" or "fix"))
        return Results.BadRequest(new { error = "Choose what you'd like to be notified about." });

    var added = store.AddToWaitlist(req.Email!, req.Organization, req.Interest, req.MarketingOk);
    return Results.Ok(new { added });
}).RequireRateLimiting("waitlist").DisableAntiforgery();

app.Run();

public sealed record WaitlistRequest(string? Email, string? Organization, string Interest, bool MarketingOk);

public partial class Program;

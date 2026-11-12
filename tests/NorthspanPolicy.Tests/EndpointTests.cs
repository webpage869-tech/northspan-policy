using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NorthspanPolicy;

namespace NorthspanPolicy.Tests;

/// <summary>Stands in for the Claude API so tests never call the real service.</summary>
internal sealed class FakeAi : IAiClient
{
    public string? LastUserPrompt { get; private set; }
    public string Reply { get; set; } =
        "{\"items\":[" + string.Join(",", Checklist.Items.Select((i, n) =>
            $"{{\"id\":\"{i.Id}\",\"status\":\"{(n < 10 ? "met" : "missing")}\",\"note\":\"missing\"}}")) + "]}";

    public Task<string> CompleteAsync(string model, string system, string user, int maxTokens, CancellationToken ct = default)
    {
        LastUserPrompt = user;
        return Task.FromResult(Reply);
    }
}

/// <summary>Records emails instead of sending them.</summary>
internal sealed class FakeMail : IEmailSender
{
    public System.Collections.Concurrent.ConcurrentBag<OutgoingEmail> Sent { get; } = new();
    public bool IsConfigured => true;
    public Task SendAsync(OutgoingEmail email, CancellationToken ct = default) { Sent.Add(email); return Task.CompletedTask; }
}

public class EndpointTests : IClassFixture<EndpointTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        internal FakeAi Ai { get; } = new();
        internal FakeMail Mail { get; } = new();
        private readonly string _db = Path.Combine(Path.GetTempPath(), $"policy-e2e-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Data:Path", _db);
            builder.UseSetting("Checks:DailyLimit", "1000");
            builder.UseSetting("Notify:OwnerEmail", "owner@example.com");
            builder.ConfigureServices(s =>
            {
                s.AddSingleton<IAiClient>(Ai);
                s.AddSingleton<IEmailSender>(Mail);
            });
        }
    }

    private readonly Factory _factory;
    private static readonly string PolicyText = string.Join(" ", Enumerable.Repeat("Staff must follow this AI policy.", 20));

    public EndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var res = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task HomePage_IsServedWithSecurityHeaders()
    {
        var res = await _factory.CreateClient().GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.Contains("X-Content-Type-Options"));
        Assert.Contains("AI Acceptable-Use Policy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Check_ReturnsPreviewWithoutHiddenGapDetails()
    {
        var form = new MultipartFormDataContent { { new StringContent(PolicyText), "text" } };
        var res = await _factory.CreateClient().PostAsync("/api/check", form);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(67, root.GetProperty("scorePercent").GetInt32()); // 10 of 15 met
        Assert.Equal(PolicyChecker.FreeGapsShown, root.GetProperty("gaps").GetArrayLength());
        Assert.Equal(5 - PolicyChecker.FreeGapsShown, root.GetProperty("hiddenGaps").GetInt32());
        Assert.Contains(PolicyText[..30], _factory.Ai.LastUserPrompt);
    }

    [Fact]
    public async Task Check_RejectsTooShortText()
    {
        var form = new MultipartFormDataContent { { new StringContent("too short"), "text" } };
        var res = await _factory.CreateClient().PostAsync("/api/check", form);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Waitlist_AcceptsValidSignUp_AndRejectsBadEmail()
    {
        var client = _factory.CreateClient();
        var ok = await client.PostAsJsonAsync("/api/waitlist",
            new { email = $"test-{Guid.NewGuid():N}@example.com", organization = "Demo", interest = "fix", marketingOk = false });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        var bad = await client.PostAsJsonAsync("/api/waitlist", new { email = "nope", interest = "fix", marketingOk = false });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Waitlist_EmailsTheOwnerOnceForANewSignUp()
    {
        var client = _factory.CreateClient();
        var email = $"notify-{Guid.NewGuid():N}@example.com";
        var body = new { email, organization = "Demo Insurance", interest = "create", marketingOk = true };

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/waitlist", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/waitlist", body)).StatusCode); // duplicate

        OutgoingEmail[] notices = [];
        for (var i = 0; i < 50 && notices.Length == 0; i++)
        {
            notices = _factory.Mail.Sent.Where(m => m.Body.Contains(email)).ToArray();
            if (notices.Length == 0) await Task.Delay(100);
        }
        await Task.Delay(200);
        notices = _factory.Mail.Sent.Where(m => m.Body.Contains(email)).ToArray();

        var notice = Assert.Single(notices); // owner only: visitor confirmation is off by default, and no repeat for the duplicate
        Assert.Equal("owner@example.com", notice.To);
        Assert.Contains("Demo Insurance", notice.Body);
    }

    [Fact]
    public async Task Config_ReportsCheckoutOffDuringSoftLaunch()
    {
        var json = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(json.GetProperty("checkoutEnabled").GetBoolean());
    }
}

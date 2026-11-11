using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NorthspanPolicy;

public interface IAiClient
{
    Task<string> CompleteAsync(string model, string system, string user, int maxTokens, CancellationToken ct = default);
}

/// <summary>
/// Minimal client for the Claude Messages API. The key comes from the ANTHROPIC_API_KEY
/// environment variable (the server's .env file) and is never logged.
/// </summary>
public sealed class AnthropicClient(HttpClient http, IConfiguration config, ILogger<AnthropicClient> log) : IAiClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    public async Task<string> CompleteAsync(string model, string system, string user, int maxTokens, CancellationToken ct = default)
    {
        var key = config["ANTHROPIC_API_KEY"];
        if (string.IsNullOrWhiteSpace(key))
            throw new CheckFailedException("The policy check is not available right now. Please try again later.");

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = user }),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", ApiVersion);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            // Log status and the error type only; never the request body (it holds the customer's policy).
            string errType = "unknown";
            try { errType = JsonNode.Parse(json)?["error"]?["type"]?.GetValue<string>() ?? errType; } catch (JsonException) { }
            log.LogWarning("Claude API returned {Status} ({ErrorType})", (int)res.StatusCode, errType);
            throw new CheckFailedException("The check could not be completed. Please try again in a minute.");
        }

        var node = JsonNode.Parse(json);
        var sb = new StringBuilder();
        IEnumerable<JsonNode?> blocks = node?["content"] is JsonArray arr ? arr : Array.Empty<JsonNode?>();
        foreach (var block in blocks)
            if (block?["type"]?.GetValue<string>() == "text")
                sb.Append(block["text"]?.GetValue<string>());
        return sb.ToString();
    }
}

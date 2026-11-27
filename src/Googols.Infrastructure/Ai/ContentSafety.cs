using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Googols.Application.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Googols.Infrastructure.Ai;

public sealed class ContentSafetyOptions
{
    public const string Section = "ContentSafety";
    public string? Endpoint { get; set; }   // https://<name>.cognitiveservices.azure.com
    public string? ApiKey { get; set; }
    /// <summary>0 safe · 2 low · 4 medium · 6 high. Children's platform: block from "low" upwards.</summary>
    public int BlockAtSeverity { get; set; } = 2;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Layered screen used before and after every AI call:
/// 1. <see cref="BasicContentSafety"/>: contact details and length.
/// 2. Self-harm phrases (English + Malayalam), so a child in distress gets a caring reply with a helpline
///    even when no cloud classifier is configured.
/// 3. Azure AI Content Safety text analysis (hate, sexual, violence, self-harm), when configured.
/// The model deployment's own Azure OpenAI content filter is a further layer on top.
/// </summary>
public sealed class LayeredContentSafety(BasicContentSafety basic, IHttpClientFactory http, IOptions<ContentSafetyOptions> options,
    ILogger<LayeredContentSafety> log) : IContentSafety
{
    private static readonly string[] SelfHarmPhrases =
    [
        "kill myself", "killing myself", "suicide", "suicidal", "want to die", "end my life", "hurt myself", "self harm", "self-harm", "cut myself",
        "ആത്മഹത്യ", "മരിക്കണം", "ജീവിതം അവസാനിപ്പിക്കാൻ",
    ];

    private readonly ContentSafetyOptions _o = options.Value;

    public async Task<SafetyVerdict> CheckAsync(string text, CancellationToken ct)
    {
        var first = await basic.CheckAsync(text, ct);
        if (!first.Allowed)
        {
            return first;
        }
        var lower = text.ToLowerInvariant();
        if (SelfHarmPhrases.Any(p => lower.Contains(p, StringComparison.Ordinal)))
        {
            return new SafetyVerdict(false, SafetyCategories.SelfHarm);
        }
        if (!_o.IsConfigured)
        {
            return first;
        }
        try
        {
            var client = http.CreateClient(nameof(LayeredContentSafety));
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_o.Endpoint!.TrimEnd('/')}/contentsafety/text:analyze?api-version=2024-09-01")
            {
                Content = JsonContent.Create(new { text, categories = new[] { "Hate", "SelfHarm", "Sexual", "Violence" }, outputType = "FourSeverityLevels" }),
            };
            req.Headers.Add("Ocp-Apim-Subscription-Key", _o.ApiKey);
            using var res = await client.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<AnalyzeResult>(ct);
            var worst = body?.CategoriesAnalysis?.Where(c => c.Severity >= _o.BlockAtSeverity).OrderByDescending(c => c.Severity).FirstOrDefault();
            return worst is null
                ? new SafetyVerdict(true, null)
                : new SafetyVerdict(false, worst.Category == "SelfHarm" ? SafetyCategories.SelfHarm : SafetyCategories.Harmful);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Fail closed for a children's platform: if the classifier is down, don't pass unscreened text.
            log.LogWarning(ex, "Content Safety call failed; blocking this message.");
            return new SafetyVerdict(false, SafetyCategories.Unavailable);
        }
    }

    private sealed class AnalyzeResult
    {
        [JsonPropertyName("categoriesAnalysis")] public List<CategoryResult>? CategoriesAnalysis { get; set; }
    }

    private sealed class CategoryResult
    {
        [JsonPropertyName("category")] public string Category { get; set; } = "";
        [JsonPropertyName("severity")] public int Severity { get; set; }
    }
}

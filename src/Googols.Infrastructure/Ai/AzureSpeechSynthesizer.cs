using System.Net.Http.Headers;
using System.Security;
using System.Text;
using Googols.Application.Ai;
using Microsoft.Extensions.Options;

namespace Googols.Infrastructure.Ai;

public sealed class SpeechOptions
{
    public const string Section = "Speech";
    public string? Region { get; set; }      // e.g. "centralindia"
    public string? ApiKey { get; set; }      // app setting Speech__ApiKey, never in the repo
    public string MalayalamVoice { get; set; } = "ml-IN-SobhanaNeural";
    public string EnglishVoice { get; set; } = "en-IN-NeerjaNeural";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Region) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Azure AI Speech text-to-speech over REST (no SDK). Used when the browser has no Malayalam voice.</summary>
public sealed class AzureSpeechSynthesizer(IOptions<SpeechOptions> options, IHttpClientFactory http) : ISpeechSynthesizer
{
    private readonly SpeechOptions _o = options.Value;

    public bool IsConfigured => _o.IsConfigured;

    public async Task<byte[]> SynthesizeAsync(string text, string language, CancellationToken ct)
    {
        if (!_o.IsConfigured)
        {
            throw new InvalidOperationException("Speech is not configured.");
        }
        var (locale, voice) = language == "ml" ? ("ml-IN", _o.MalayalamVoice) : ("en-IN", _o.EnglishVoice);
        var ssml = $"<speak version='1.0' xml:lang='{locale}'><voice name='{voice}'>{SecurityElement.Escape(text)}</voice></speak>";
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{_o.Region}.tts.speech.microsoft.com/cognitiveservices/v1")
        {
            Content = new StringContent(ssml, Encoding.UTF8),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/ssml+xml");
        req.Headers.Add("Ocp-Apim-Subscription-Key", _o.ApiKey);
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        req.Headers.UserAgent.ParseAdd("googols");
        using var res = await http.CreateClient(nameof(AzureSpeechSynthesizer)).SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsByteArrayAsync(ct);
    }
}

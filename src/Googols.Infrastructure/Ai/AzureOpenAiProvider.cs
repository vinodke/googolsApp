using Azure;
using Azure.AI.OpenAI;
using Googols.Application.Ai;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace Googols.Infrastructure.Ai;

public sealed class AzureOpenAiOptions
{
    public const string Section = "AzureOpenAI";
    public string? Endpoint { get; set; }
    public string? Deployment { get; set; }
    public string? ApiKey { get; set; }   // set via user-secrets / Key Vault, never appsettings.json
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Deployment) && !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class AiNotConfiguredException() : Exception("The AI provider is not configured on this server.");

public sealed class AzureOpenAiProvider(IOptions<AzureOpenAiOptions> options) : IAiModelProvider
{
    private readonly AzureOpenAiOptions _o = options.Value;
    private ChatClient? _chat;

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        if (!_o.IsConfigured)
        {
            throw new AiNotConfiguredException();
        }
        _chat ??= new AzureOpenAIClient(new Uri(_o.Endpoint!), new AzureKeyCredential(_o.ApiKey!)).GetChatClient(_o.Deployment!);

        var messages = new List<ChatMessage> { new SystemChatMessage(request.SystemPrompt) };
        foreach (var turn in request.History)
        {
            messages.Add(turn.Role == "assistant" ? new AssistantChatMessage(turn.Text) : new UserChatMessage(turn.Text));
        }
        messages.Add(new UserChatMessage(request.UserMessage));

        var result = await _chat.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens }, ct);
        var completion = result.Value;
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : "";
        return new AiCompletionResult(text, completion.Usage?.InputTokenCount ?? 0, completion.Usage?.OutputTokenCount ?? 0, completion.Model);
    }
}

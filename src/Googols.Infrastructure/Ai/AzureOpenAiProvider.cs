using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text;
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

    private ChatClient Chat()
    {
        if (!_o.IsConfigured)
        {
            throw new AiNotConfiguredException();
        }
        return _chat ??= new AzureOpenAIClient(new Uri(_o.Endpoint!), new AzureKeyCredential(_o.ApiKey!)).GetChatClient(_o.Deployment!);
    }

    private static List<ChatMessage> Messages(AiCompletionRequest request)
    {
        var messages = new List<ChatMessage> { new SystemChatMessage(request.SystemPrompt) };
        foreach (var turn in request.History)
        {
            messages.Add(turn.Role == "assistant" ? new AssistantChatMessage(turn.Text) : new UserChatMessage(turn.Text));
        }
        messages.Add(request.Image is { } img
            ? new UserChatMessage(
                ChatMessageContentPart.CreateTextPart(request.UserMessage),
                ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(img.Data), img.MediaType, ChatImageDetailLevel.High))
            : new UserChatMessage(request.UserMessage));
        return messages;
    }

    private static bool IsContentFilter(ClientResultException ex) =>
        ex.Status == 400 && ex.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase);

    public async Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct)
    {
        var chat = Chat();
        ClientResult<ChatCompletion> result;
        try
        {
            result = await chat.CompleteChatAsync(Messages(request), new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens }, ct);
        }
        catch (ClientResultException ex) when (IsContentFilter(ex))
        {
            throw new AiContentFilteredException(); // Azure OpenAI's own filter rejected the prompt
        }
        var completion = result.Value;
        if (completion.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new AiContentFilteredException(); // the answer was cut by the output filter
        }
        var text = completion.Content.Count > 0 ? completion.Content[0].Text : "";
        return new AiCompletionResult(text, completion.Usage?.InputTokenCount ?? 0, completion.Usage?.OutputTokenCount ?? 0, completion.Model);
    }

    public async IAsyncEnumerable<AiStreamUpdate> StreamAsync(AiCompletionRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var chat = Chat();
        var messages = Messages(request);
        var text = new StringBuilder();
        int inputTokens = 0, outputTokens = 0;
        var model = _o.Deployment!;

        await using var updates = chat.CompleteChatStreamingAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = request.MaxOutputTokens }, ct)
            .GetAsyncEnumerator(ct);
        while (true)
        {
            StreamingChatCompletionUpdate update;
            try
            {
                if (!await updates.MoveNextAsync())
                {
                    break;
                }
                update = updates.Current;
            }
            catch (ClientResultException ex) when (IsContentFilter(ex))
            {
                throw new AiContentFilteredException();
            }
            if (update.Usage is { } usage)
            {
                inputTokens = usage.InputTokenCount;
                outputTokens = usage.OutputTokenCount;
            }
            if (!string.IsNullOrEmpty(update.Model))
            {
                model = update.Model;
            }
            if (update.FinishReason == ChatFinishReason.ContentFilter)
            {
                throw new AiContentFilteredException();
            }
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    text.Append(part.Text);
                    yield return new AiStreamUpdate(part.Text, null);
                }
            }
        }

        if (inputTokens == 0 && outputTokens == 0)
        {
            // Some API versions omit usage on streams; estimate so the cost log isn't empty.
            inputTokens = AiCost.EstimateTokens(request.SystemPrompt) + request.History.Sum(h => AiCost.EstimateTokens(h.Text))
                + AiCost.EstimateTokens(request.UserMessage) + (request.Image is null ? 0 : 1000);
            outputTokens = AiCost.EstimateTokens(text.ToString());
        }
        yield return new AiStreamUpdate(null, new AiCompletionResult(text.ToString(), inputTokens, outputTokens, model));
    }
}

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeDutiesAssistant.Configuration;
using Microsoft.Extensions.Options;

namespace HomeDutiesAssistant.Services;

public sealed class OllamaClient(IHttpClientFactory httpClientFactory, IOptions<OllamaOptions> options)
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();
    private readonly string _chatModel = options.Value.ChatModel;
    private readonly string _embeddingModel = options.Value.EmbeddingModel;
    private readonly string _documentPrefix = options.Value.EmbedDocumentPrefix;
    private readonly string _queryPrefix = options.Value.EmbedQueryPrefix;
    private readonly string _chatUri = options.Value.ChatUri;
    private readonly string _embedUri = options.Value.EmbedUri;

    public Task<float[]> EmbedDocumentAsync(string text, CancellationToken ct = default)
        => EmbedAsync(_documentPrefix + text, ct);

    public Task<float[]> EmbedQueryAsync(string text, CancellationToken ct = default)
        => EmbedAsync(_queryPrefix + text, ct);

    // POST /api/embed -> { "embeddings": [[...768 floats...]] }
    // We send one string and take the single vector back.
    private async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var request = new { model = _embeddingModel, input = text };
        using var resp = await _httpClient.PostAsJsonAsync(_embedUri, request, ct);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty embedding response from Ollama.");
        if (payload.Embeddings.Length == 0)
            throw new InvalidOperationException("Ollama returned no embedding.");
        return payload.Embeddings[0];
    }

    // POST /api/chat with stream=true. Ollama replies with one JSON object per
    // line (NDJSON); each carries a little piece of the answer. We yield each
    // piece as it arrives so the CLI can print the answer as it is written.
    public async IAsyncEnumerable<string> ChatStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new
        {
            model = _chatModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            stream = true
        };

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, _chatUri);
        httpReq.Content = JsonContent.Create(request);

        // ResponseHeadersRead = start reading the body before it is fully sent.
        using var resp = await _httpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var chunk = JsonSerializer.Deserialize<ChatStreamChunk>(line);
            if (chunk?.Message?.Content is { Length: > 0 } content)
                yield return content;
            if (chunk?.Done == true)
                yield break;
        }
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")] public float[][] Embeddings { get; set; } = [];
    }

    private sealed class ChatStreamChunk
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }
}

// One turn in a conversation. Roles: "system", "user", "assistant".
public sealed class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("content")] public string Content { get; set; } = "";

    public ChatMessage() { }
    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
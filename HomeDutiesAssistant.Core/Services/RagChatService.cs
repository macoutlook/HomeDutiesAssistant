using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using Microsoft.Extensions.Options;

namespace HomeDutiesAssistant.Services;

// The RAG core: retrieve relevant facts from the vector DB, augment a prompt
// with them, and produce a grounded answer. This service is deliberately
// input-layer agnostic — it knows nothing about CLI, HTTP, or any other
// transport. Callers pass a question plus prior turns and decide how to render
// the streamed answer and how to persist the conversation.
public sealed class RagChatService(OllamaClient ollama, DutiesVector db, IOptions<RagOptions> ragOptions)
{
    private readonly RagOptions _rag = ragOptions.Value;

    // Answer a single turn. The returned task completes once retrieval is done
    // (so callers can wrap that latency in a spinner/await); the answer itself
    // is exposed as a lazy token stream the caller consumes however it likes.
    public async Task<RagAnswer> AskAsync(
        string question,
        IReadOnlyList<ChatMessage> history,
        CancellationToken ct = default)
    {
        // RAG step 1 — RETRIEVE: embed the question and fetch the closest facts.
        var queryEmbedding = await ollama.EmbedQueryAsync(question, ct);
        var hits = await db.SearchAsync(queryEmbedding, question, _rag.TopK, ct);

        // RAG step 2 — AUGMENT: pin the model to ONLY those facts.
        var facts = string.Join("\n", hits.Select(h => $"- {h.Content}"));
        var systemPrompt = $"""
            You are a household assistant. Answer the user's question using ONLY
            the facts listed below. If the answer is not contained in the facts,
            say you do not have that information. Be concise and concrete: quote
            amounts, currencies and dates exactly as written.
            Today's date is {DateTime.Now:yyyy-MM-dd}.

            FACTS:
            {facts}
            """;

        var messages = new List<ChatMessage> { new("system", systemPrompt) };
        messages.AddRange(history);          // short-term conversation memory
        messages.Add(new("user", question));

        // RAG step 3 — GENERATE: hand back the retrieved sources plus a lazy
        // token stream. Generation starts when the caller enumerates Tokens.
        return new RagAnswer(hits, ollama.ChatStreamAsync(messages, ct));
    }
}

// The result of a single RAG turn: the facts used to ground the answer and the
// answer itself as a stream of tokens. Returned after retrieval completes.
public sealed record RagAnswer(IReadOnlyList<SearchHit> Sources, IAsyncEnumerable<string> Tokens);
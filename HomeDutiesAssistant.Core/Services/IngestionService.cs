using HomeDutiesAssistant.Infrastructure;

namespace HomeDutiesAssistant.Services;

// Loads the YAML facts, turns each into an embedding via Ollama, and stores it
// in the vector database — the "build the knowledge base" step. Like the RAG
// core, this is input-layer agnostic: progress is surfaced through IProgress<T>
// and the record count is returned, so the caller (CLI, API, …) decides how to
// render it.
public sealed class IngestionService(OllamaClient ollama, DutiesVector db, DataLoader loader)
{
    // Embed and store every record found under dataDir. Returns the number of
    // records stored (0 if none were found). Reports per-record progress to the
    // optional progress sink.
    public async Task<int> IngestAsync(
        string dataDir,
        IProgress<IngestionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var records = loader.LoadFromDirectory(dataDir);
        var total = records.Count;
        var completed = 0;

        foreach (var record in records)
        {
            var text = record.ToContext();
            var embedding = await ollama.EmbedDocumentAsync(text, ct);   // text -> 768-dim vector
            await db.UpsertAsync(record, text, embedding, ct);
            progress?.Report(new IngestionProgress(++completed, total, record.Category));
        }

        return total;
    }
}

// Progress for a single ingestion run: how many records are done out of the
// total, plus the category just stored.
public sealed record IngestionProgress(int Completed, int Total, string Category);
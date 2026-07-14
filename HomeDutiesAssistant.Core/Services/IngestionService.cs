using HomeDutiesAssistant.Infrastructure;

namespace HomeDutiesAssistant.Services;

// Loads the YAML facts, turns each into an embedding via Ollama, and stores it
// in the vector database.
public sealed class IngestionService(OllamaClient ollama, DutiesRepository db, DataLoader loader)
{
    // Embed and store every record found under dataDir.
    public async Task<int> IngestAsync(
        string dataDir,
        long homeId,
        IProgress<IngestionProgress>? progress = null,
        CancellationToken ct = default)
    {
        var records = loader.LoadFromDirectory(dataDir);
        var total = records.Count;
        var completed = 0;

        foreach (var record in records)
        {
            record.HomeId = homeId;
            var text = record.ToContext();
            var embedding = await ollama.EmbedDocumentAsync(text, ct);
            await db.SaveAsync(record, text, embedding, ct);
            progress?.Report(new IngestionProgress(++completed, total, record.Category));
        }

        return total;
    }
}

// Progress for a single ingestion run: how many records are done out of the
// total, plus the category just stored.
public sealed record IngestionProgress(int Completed, int Total, string Category);
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;

namespace HomeDutiesAssistant.Services;

// CRUD over the duties knowledge base for the management UI. Saving recomputes
// the canonical content sentence and re-embeds it, so a duty stays queryable by
// the chat the moment it is created or edited. Transport-agnostic like the rest
// of the core: callers decide how to render and when to call it.
public sealed class DutyService(OllamaClient ollama, DutiesVector db)
{
    public Task<List<Duty>> ListAsync(CancellationToken ct = default)
        => db.ListDutiesAsync(ct);

    // Create (Id == 0) or update (Id > 0) a duty. Creates dedupe on the unique
    // title; the database assigns the id, written back onto the returned Duty.
    public async Task<Duty> SaveAsync(Duty duty, CancellationToken ct = default)
    {
        var content = duty.ToContext();                              // canonical fact text
        var embedding = await ollama.EmbedDocumentAsync(content, ct); // text -> vector
        await db.UpsertAsync(duty, content, embedding, ct);
        return duty;
    }

    public Task<bool> DeleteAsync(long id, CancellationToken ct = default)
        => db.DeleteAsync(id, ct);
}
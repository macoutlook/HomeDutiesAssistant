using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;

namespace HomeDutiesAssistant.Services;

public sealed class DutyService(OllamaClient ollama, DutiesRepository db)
{
    public async Task<Duty> SaveAsync(Duty duty, CancellationToken ct = default)
    {
        if (duty.Id <= 0 && await db.CountAsync(duty.HomeId, ct) >= HomeLimits.MaxDuties)
            throw new InvalidOperationException(
                $"This home has reached its limit of {HomeLimits.MaxDuties} duties.");

        var content = duty.ToContext();                              // canonical fact text
        var embedding = await ollama.EmbedDocumentAsync(content, ct); // text -> vector
        await db.SaveAsync(duty, content, embedding, ct);
        return duty;
    }

    public Task<bool> DeleteAsync(long id, long homeId, CancellationToken ct = default)
        => db.DeleteAsync(id, homeId, ct);

    public Task<List<Duty>> ListAsync(long homeId, CancellationToken ct = default)
        => db.ListDutiesAsync(homeId, ct);

    public Task<long> CountAsync(long homeId, CancellationToken ct = default)
        => db.CountAsync(homeId, ct);
}
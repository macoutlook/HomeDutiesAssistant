using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;
using Task = System.Threading.Tasks.Task;

namespace HomeDutiesAssistant.Services;

public sealed class HomeService(HomesRepository repository)
{
    private const string DefaultHomeName = "Home";

    public Task<Home> CreateAsync(string name, CancellationToken ct = default)
        => repository.CreateAsync(name, ct);

    public Task AssignAsync(string userId, long homeId, CancellationToken ct = default)
        => repository.SetUserHomeAsync(userId, homeId, ct);

    public Task DeleteAsync(long homeId, CancellationToken ct = default)
        => repository.DeleteAsync(homeId, ct);
    
    public Task<Home?> GetUserHomeAsync(string userId, CancellationToken ct = default)
        => repository.GetUserHomeAsync(userId, ct);

    public Task<List<string>> ListUserIdsAsync(long homeId, CancellationToken ct = default)
        => repository.ListUserIdsAsync(homeId, ct);

    // Fallback home for the console (no auth) and first-run seeding.
    public async Task<Home> GetDefaultAsync(CancellationToken ct = default)
        => await repository.GetByNameAsync(DefaultHomeName, ct)
           ?? await repository.CreateAsync(DefaultHomeName, ct);

    public Task<List<Home>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);
}
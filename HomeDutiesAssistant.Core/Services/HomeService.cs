using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;

namespace HomeDutiesAssistant.Services;

public sealed class HomeService(HomesRepository repository)
{
    private const string DefaultHomeName = "Home";

    public Task<Home> CreateAsync(string name, CancellationToken ct = default)
        => repository.CreateAsync(name, ct);

    // The fallback Home used until per-user Home assignment lands.
    public async Task<Home> GetDefaultAsync(CancellationToken ct = default)
        => await repository.GetByNameAsync(DefaultHomeName, ct)
           ?? await repository.CreateAsync(DefaultHomeName, ct);

    public Task<List<Home>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);
}
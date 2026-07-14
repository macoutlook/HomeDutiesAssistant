using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HomeDutiesAssistant.Infrastructure;

public sealed class HomesRepository(IOptions<DatabaseOptions> database)
    : IDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(database.Value.ConnectionString);

    public async Task<Home> CreateAsync(string name, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomesSql.Insert);
        cmd.Parameters.AddWithValue(name);
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return new Home { Id = id, Name = name };
    }
    
    public async Task<Home?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomesSql.SelectByName);
        cmd.Parameters.AddWithValue(name);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new Home { Id = reader.GetInt64(0), Name = reader.GetString(1) }
            : null;
    }

    public async Task<List<Home>> ListAsync(CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomesSql.List);
        var homes = new List<Home>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            homes.Add(new Home { Id = reader.GetInt64(0), Name = reader.GetString(1) });
        return homes;
    }

    public void Dispose() => _dataSource.Dispose();
}
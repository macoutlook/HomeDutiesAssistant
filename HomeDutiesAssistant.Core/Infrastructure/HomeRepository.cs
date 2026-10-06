using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HomeDutiesAssistant.Infrastructure;

public sealed class HomeRepository(IOptions<DatabaseOptions> databaseOptions)
    : IDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(databaseOptions.Value.ConnectionString);

    public async Task<Home> SaveAsync(string name, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.Insert);
        cmd.Parameters.AddWithValue(name);
        var id = (long)(await cmd.ExecuteScalarAsync(ct))!;
        return new Home { Id = id, Name = name };
    }
    
    public async System.Threading.Tasks.Task SetUserHomeAsync(string userId, long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.InsertUserHome);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(homeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async System.Threading.Tasks.Task DeleteAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.Delete);
        cmd.Parameters.AddWithValue(homeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Home?> GetUserHomeAsync(string userId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.SelectHomeByUser);
        cmd.Parameters.AddWithValue(userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new Home { Id = reader.GetInt64(0), Name = reader.GetString(1) }
            : null;
    }

    public async Task<List<string>> ListUserIdsAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.SelectUserIdsByHome);
        cmd.Parameters.AddWithValue(homeId);
        var ids = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetString(0));
        return ids;
    }

    public async Task<Home?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.SelectByName);
        cmd.Parameters.AddWithValue(name);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new Home { Id = reader.GetInt64(0), Name = reader.GetString(1) }
            : null;
    }

    public async Task<List<Home>> ListAsync(CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(HomeSql.List);
        var homes = new List<Home>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            homes.Add(new Home { Id = reader.GetInt64(0), Name = reader.GetString(1) });
        return homes;
    }

    public void Dispose() => _dataSource.Dispose();
}
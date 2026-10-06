using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Models;
using Microsoft.Extensions.Options;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace HomeDutiesAssistant.Infrastructure;

public sealed class PushRepository(IOptions<DatabaseOptions> database) : IDisposable
{
    private readonly NpgsqlDataSource _dataSource = NpgsqlDataSource.Create(database.Value.ConnectionString);

    public async Task SaveAsync(WebPushSubscription subscription, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.Upsert);
        cmd.Parameters.AddWithValue(subscription.UserId);
        cmd.Parameters.AddWithValue(subscription.HomeId);
        cmd.Parameters.AddWithValue(subscription.Endpoint);
        cmd.Parameters.AddWithValue(subscription.EncryptionKey);
        cmd.Parameters.AddWithValue(subscription.Auth);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteAsync(string endpoint, string userId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.Delete);
        cmd.Parameters.AddWithValue(endpoint);
        cmd.Parameters.AddWithValue(userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeleteByEndpointAsync(string endpoint, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.DeleteByEndpoint);
        cmd.Parameters.AddWithValue(endpoint);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> CountByUserAsync(string userId, string exceptEndpoint, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.CountByUser);
        cmd.Parameters.AddWithValue(userId);
        cmd.Parameters.AddWithValue(exceptEndpoint);
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<List<WebPushSubscription>> ListByHomeAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.ListByHome);
        cmd.Parameters.AddWithValue(homeId);
        var subscriptions = new List<WebPushSubscription>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            subscriptions.Add(new WebPushSubscription(
                reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        }
        return subscriptions;
    }

    public async Task<List<Reminder>> ListDueAsync(DateOnly today, DateOnly lead, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(PushSql.ListDue);
        cmd.Parameters.AddWithValue(today);
        cmd.Parameters.AddWithValue(lead);
        var reminders = new List<Reminder>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            reminders.Add(new Reminder(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetFieldValue<DateOnly>(3)));
        }
        return reminders;
    }

    public void Dispose() => _dataSource.Dispose();
}

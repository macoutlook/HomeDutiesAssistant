using HomeDutiesAssistant.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using TaskModel = HomeDutiesAssistant.Models.Task;
using TaskStatus = HomeDutiesAssistant.Models.TaskStatus;

namespace HomeDutiesAssistant.Infrastructure;

public sealed class TasksRepository : IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public TasksRepository(IOptions<DatabaseOptions> database)
        => _dataSource = NpgsqlDataSource.Create(database.Value.ConnectionString);

    // Create (Id == 0) or update (Id > 0). On create the DB assigns the id, which
    // is written back onto the task.
    public async Task SaveAsync(TaskModel task, CancellationToken ct = default)
    {
        if (task.Id <= 0)
        {
            await using var cmd = _dataSource.CreateCommand(TasksSql.Insert);
            cmd.Parameters.AddWithValue(task.HomeId);
            cmd.Parameters.AddWithValue(task.Title);
            cmd.Parameters.AddWithValue(task.DueDate != null ? task.DueDate : DBNull.Value);
            cmd.Parameters.AddWithValue(task.Status.ToString());
            task.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        else
        {
            await using var cmd = _dataSource.CreateCommand(TasksSql.Update);
            cmd.Parameters.AddWithValue(task.Id);
            cmd.Parameters.AddWithValue(task.HomeId);
            cmd.Parameters.AddWithValue(task.Title);
            cmd.Parameters.AddWithValue(task.DueDate != null ? task.DueDate : DBNull.Value);
            cmd.Parameters.AddWithValue(task.Status.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<bool> DeleteAsync(long id, long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(TasksSql.Delete);
        cmd.Parameters.AddWithValue(id);
        cmd.Parameters.AddWithValue(homeId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    // Persist a new order: each id's priority becomes its position in the list.
    public async Task ReorderAsync(long homeId, IReadOnlyList<long> orderedIds, CancellationToken ct = default)
    {
        if (orderedIds.Count == 0) return;
        await using var cmd = _dataSource.CreateCommand(TasksSql.Reorder);
        cmd.Parameters.AddWithValue(homeId);
        cmd.Parameters.AddWithValue(orderedIds.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<TaskModel>> ListAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(TasksSql.List);
        cmd.Parameters.AddWithValue(homeId);
        var tasks = new List<TaskModel>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tasks.Add(new TaskModel
            {
                Id = reader.GetInt64(0),
                HomeId = reader.GetInt64(1),
                Title = reader.GetString(2),
                DueDate = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3),
                Status = Enum.TryParse<TaskStatus>(reader.GetString(4), out var status) ? status : TaskStatus.Todo,
                Priority = reader.GetInt32(5),
            });
        }
        return tasks;
    }

    public async Task<long> CountAsync(long homeId, CancellationToken ct = default)
    {
        await using var cmd = _dataSource.CreateCommand(TasksSql.Count);
        cmd.Parameters.AddWithValue(homeId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public void Dispose() => _dataSource.Dispose();
}
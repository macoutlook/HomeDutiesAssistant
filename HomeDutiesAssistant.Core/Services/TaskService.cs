using HomeDutiesAssistant.Infrastructure;
using HomeLimits = HomeDutiesAssistant.Models.HomeLimits;
using TaskModel = HomeDutiesAssistant.Models.Task;

namespace HomeDutiesAssistant.Services;

public sealed class TaskService(TasksRepository repository)
{
    public async Task SaveAsync(TaskModel task, CancellationToken ct = default)
    {
        if (task.Id <= 0 && await repository.CountAsync(task.HomeId, ct) >= HomeLimits.MaxTasks)
            throw new InvalidOperationException(
                $"This home has reached its limit of {HomeLimits.MaxTasks} tasks.");
        await repository.SaveAsync(task, ct);
    }

    public Task<bool> DeleteAsync(long id, long homeId, CancellationToken ct = default)
        => repository.DeleteAsync(id, homeId, ct);

    public Task ReorderAsync(long homeId, IReadOnlyList<long> orderedIds, CancellationToken ct = default)
        => repository.ReorderAsync(homeId, orderedIds, ct);

    public Task<List<TaskModel>> ListAsync(long homeId, CancellationToken ct = default)
        => repository.ListAsync(homeId, ct);
}
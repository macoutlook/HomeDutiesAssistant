using HomeDutiesAssistant.Services;
using Quartz;

namespace HomeDutiesAssistant.Web.Jobs;

// Quartz job that seeds the knowledge base from the YAML facts. 
// To force a re-seed, empty the duties table.
[DisallowConcurrentExecution]
public sealed class IngestionJob(
    HomeService homeService,
    DutyService dutyService,
    IngestionService ingestion,
    ILogger<IngestionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        var home = await homeService.GetDefaultAsync(ct);

        if (await dutyService.CountAsync(home.Id, ct) > 0)
        {
            logger.LogInformation("Knowledge base already populated — skipping seed.");
            return;
        }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        // Seed the YAML facts into the default home.
        var stored = await ingestion.IngestAsync(dataDir, home.Id, progress: null, ct);

        logger.LogInformation(
            "Seeded {Count} records from {DataDir}.", stored, dataDir);
    }
}
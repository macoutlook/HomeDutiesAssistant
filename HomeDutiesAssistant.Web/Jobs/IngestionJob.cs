using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Services;
using Quartz;

namespace HomeDutiesAssistant.Web.Jobs;

// Quartz job that seeds the knowledge base from the YAML facts. It owns the
// idempotent schema-ensure so there is no separate startup step. Since the DB
// is now the source of truth (duties are created/edited in the UI), this only
// seeds when the table is empty — so it never reverts user edits on its 6-hour
// tick. To force a re-seed, empty the duties table.
[DisallowConcurrentExecution] // never let two ingestion runs overlap
public sealed class IngestionJob(
    DutiesVector db,
    IngestionService ingestion,
    ILogger<IngestionJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        await db.InitializeAsync(ct); // idempotent: CREATE ... IF NOT EXISTS

        if (await db.CountAsync(ct) > 0)
        {
            logger.LogInformation("Knowledge base already populated — skipping seed.");
            return;
        }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var stored = await ingestion.IngestAsync(dataDir, progress: null, ct);

        logger.LogInformation(
            "Seeded {Count} records from {DataDir}.", stored, dataDir);
    }
}
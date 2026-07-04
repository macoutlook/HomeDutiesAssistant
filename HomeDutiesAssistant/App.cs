using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Services;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace HomeDutiesAssistant;

// Root application service, registered as a hosted service so the generic host
// starts it for us via host.RunAsync(). We stop the host once work completes,
// and surface the result through the process exit code.
public sealed class App(
    string[] args,
    DutiesVector db,
    IngestionService ingestion,
    ConsoleChat chat,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Environment.ExitCode = await RunAsync(stoppingToken);
        }
        finally
        {
            lifetime.StopApplication(); // lets host.RunAsync() return
        }
    }

    private async Task<int> RunAsync(CancellationToken ct)
    {
        // Make sure the RAG database (extension + table) is reachable & ready.
        try
        {
            await db.InitializeAsync(ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Could not connect to PostgreSQL.[/]");
            AnsiConsole.MarkupLine("Is the database container running?  ->  [yellow]docker compose up -d[/]");
            AnsiConsole.MarkupLine($"[grey]Details:[/] {ex.Message.EscapeMarkup()}");
            return 1;
        }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "chat";

        //   "ingest"            -> (re)embed and store everything in /data, then exit
        //   (no args) / "chat"  -> chat (auto-ingests on first run)
        if (command == "ingest")
        {
            await IngestWithProgressAsync(dataDir, ct);
            return 0;
        }

        if (await db.CountAsync(ct) == 0)
        {
            AnsiConsole.MarkupLine("[grey]Knowledge base is empty — ingesting data first...[/]");
            await IngestWithProgressAsync(dataDir, ct);
        }

        await chat.RunAsync(ct);
        return 0;
    }

    // CLI rendering for ingestion: drive a Spectre progress bar from the
    // IngestionService's progress reports, then print the outcome.
    private async Task IngestWithProgressAsync(string dataDir, CancellationToken ct)
    {
        var stored = 0;
        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                ProgressTask? task = null;
                // Synchronous sink: reports arrive on the ingestion thread, so
                // the live display is updated in step with the work.
                var progress = new SyncProgress<IngestionProgress>(p =>
                {
                    task ??= ctx.AddTask("[green]Embedding & storing records[/]", maxValue: p.Total);
                    task.Description = $"[green]Embedding & storing[/] [grey]({p.Category.EscapeMarkup()})[/]";
                    task.Value = p.Completed;
                });
                stored = await ingestion.IngestAsync(dataDir, progress, ct);
            });

        if (stored == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No .yaml records found in[/] {dataDir.EscapeMarkup()}");
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Stored [bold]{stored}[/] records in the knowledge base.");
        AnsiConsole.WriteLine();
    }

    // An IProgress<T> that invokes its handler synchronously on the calling
    // thread, unlike the framework's Progress<T> which posts to the captured
    // context / thread pool.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
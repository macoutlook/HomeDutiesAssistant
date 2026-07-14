using HomeDutiesAssistant.Services;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace HomeDutiesAssistant;

public sealed class App(
    string[] args,
    HomeService homeService,
    DutyService dutyService,
    IngestionService ingestion,
    ConsoleChat chat,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    private const int SUCCESS = 0;
    private const int FAILURE = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Environment.ExitCode = await RunAsync(stoppingToken);
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    private async Task<int> RunAsync(CancellationToken ct)
    {
        // The schema is created by db/init when the DB container first starts, not by the app.
        long homeId;
        try
        {
            homeId = (await homeService.GetDefaultAsync(ct)).Id;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Could not reach the database, or its schema is missing.[/]");
            AnsiConsole.MarkupLine("Is the DB container up and initialized?  ->  [yellow]docker compose up -d pgvector[/]");
            AnsiConsole.MarkupLine($"[grey]Details:[/] {ex.Message.EscapeMarkup()}");
            return FAILURE;
        }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var command = args.Length > SUCCESS ? args[SUCCESS].ToLowerInvariant() : "chat";

        //   "ingest"            -> (re)embed and store everything in /data, then exit
        //   (no args) / "chat"  -> chat (auto-ingests on first run)
        if (command == "ingest")
        {
            await IngestWithProgressAsync(dataDir, homeId, ct);
            return SUCCESS;
        }

        if (await dutyService.CountAsync(homeId, ct) == 0)
        {
            AnsiConsole.MarkupLine("[grey]Knowledge base is empty — ingesting data first...[/]");
            await IngestWithProgressAsync(dataDir, homeId, ct);
        }

        await chat.RunAsync(homeId, ct);
        return SUCCESS;
    }

    // IngestionService's progress reports, then print the outcome.
    private async Task IngestWithProgressAsync(string dataDir, long homeId, CancellationToken ct)
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
                stored = await ingestion.IngestAsync(dataDir, homeId, progress, ct);
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
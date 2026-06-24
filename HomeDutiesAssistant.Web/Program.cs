using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Services;
using HomeDutiesAssistant.Web.Components;
using HomeDutiesAssistant.Web.Jobs;
using Microsoft.Extensions.Options;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server: interactive components driven over a SignalR circuit.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- RAG core wiring: the same registrations the console app uses. ---
// Bind appsettings.json sections to typed options.
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

builder.Services.AddHttpClient(Options.DefaultName, (sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    http.BaseAddress = new Uri(options.BaseUrl);
    http.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<DutiesVector>();
builder.Services.AddSingleton<DataLoader>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddTransient<RagChatService>();
builder.Services.AddScoped<DutyService>();

// --- Scheduled ingestion: rebuild the knowledge base every 6 hours. ---
builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey(nameof(IngestionJob));
    q.AddJob<IngestionJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(t => t
        .ForJob(jobKey)
        .WithIdentity($"{nameof(IngestionJob)}-trigger")
        .StartNow()
        .WithSimpleSchedule(s => s
            .WithIntervalInHours(6)
            .RepeatForever()));
});
builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
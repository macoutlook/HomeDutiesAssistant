using System.Text;
using HomeDutiesAssistant;
using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

Console.OutputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);

// Keep framework logging out of the chat UI.
builder.Logging.SetMinimumLevel(LogLevel.Information);
// HttpClient logs every request/response at Information, which interleaves
// with the streamed chat answer — quiet it to Warning.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

// All DI registrations grouped in one lambda, then applied to the container.
Action<IServiceCollection> configure = services =>
{
    // Command-line args, so the hosted App can pick a command (chat/ingest).
    services.AddSingleton(args);

    // Configuration: bind appsettings.json sections to typed options.
    services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
    services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

    // Configure the default HttpClient (base address + timeout) that
    // OllamaClient resolves via IHttpClientFactory.CreateClient(). The Ollama
    // URIs in config are relative, so the base address lives here.
    services.AddHttpClient(Options.DefaultName, (sp, http) =>
    {
        var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.Timeout = TimeSpan.FromMinutes(5); // local models can be slow to warm up
    });
    services.AddSingleton<OllamaClient>();

    // Application services.
    services.AddSingleton<DutiesVector>();   // holds the pooled NpgsqlDataSource
    services.AddSingleton<DataLoader>();
    services.AddTransient<IngestionService>();
    services.AddTransient<RagChatService>();
    services.AddTransient<ConsoleChat>();      // CLI front-end over the RAG core

    // App runs as a hosted service: host.RunAsync() starts it for us.
    services.AddHostedService<App>();
};

configure(builder.Services);

using var host = builder.Build();

await host.RunAsync();
return Environment.ExitCode;
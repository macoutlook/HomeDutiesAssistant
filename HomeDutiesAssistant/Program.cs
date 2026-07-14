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

builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

Action<IServiceCollection> configure = services =>
{
    services.AddSingleton(args);

    // Configuration: bind appsettings.json sections to typed options.
    services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
    services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
    services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

    // Configure the default HttpClient (base address + timeout)
    services.AddHttpClient(Options.DefaultName, (sp, http) =>
    {
        var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
        http.BaseAddress = new Uri(options.BaseUrl);
        http.Timeout = TimeSpan.FromMinutes(5); // local models can be slow to warm up
    });
    services.AddSingleton<OllamaClient>();

    // Application services.
    services.AddSingleton<DutiesRepository>();
    services.AddSingleton<HomesRepository>();
    services.AddSingleton<HomeService>();
    services.AddSingleton<DutyService>();
    services.AddSingleton<DataLoader>();
    services.AddTransient<IngestionService>();
    services.AddTransient<RagChatService>();
    services.AddTransient<ConsoleChat>();

    // App runs as a hosted service: host.RunAsync() starts it for us.
    services.AddHostedService<App>();
};

configure(builder.Services);

using var host = builder.Build();

await host.RunAsync();
return Environment.ExitCode;
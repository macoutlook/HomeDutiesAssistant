using HomeDutiesAssistant.Configuration;
using HomeDutiesAssistant.Infrastructure;
using HomeDutiesAssistant.Models;
using HomeDutiesAssistant.Services;
using HomeDutiesAssistant.Web.Auth;
using HomeDutiesAssistant.Web.Components;
using HomeDutiesAssistant.Web.Data;
using HomeDutiesAssistant.Web.Jobs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logging: levels from the "Serilog" config section, compact
// JSON to the console (Docker collects stdout), and — when a Seq server URL is
// configured — also shipped to Seq for a searchable UI. Enriched with the
// ambient LogContext (request id + user, pushed per request below).
builder.Services.AddSerilog((services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter());

    var seqServerUrl = builder.Configuration["Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqServerUrl))
        loggerConfiguration.WriteTo.Seq(seqServerUrl);
});

// Persist Data Protection keys (used to encrypt antiforgery tokens, etc.) to a
// stable location so they survive container recreation
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("HomeDutiesAssistant");
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath));

// Blazor Server: interactive components driven over a SignalR circuit.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- RAG core wiring: the same registrations the console app uses. ---
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));

builder.Services.AddHttpClient(Options.DefaultName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
    httpClient.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<DutiesVector>();
builder.Services.AddSingleton<DataLoader>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddTransient<RagChatService>();
builder.Services.AddScoped<DutyService>();

// --- Authentication / authorization ---
var authConfigurationSection = builder.Configuration.GetSection(Auth.SectionName);
builder.Services.Configure<Auth>(authConfigurationSection);
var authSettings = authConfigurationSection.Get<Auth>() ?? new Auth();

// Fail fast if a production deployment hasn't supplied a real signing key
// (expected via the Auth__Jwt__SigningKey environment variable).
if (builder.Environment.IsProduction() &&
    System.Text.Encoding.UTF8.GetByteCount(authSettings.Jwt.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Auth__Jwt__SigningKey must be provided (>= 32 bytes) via environment in Production.");
}

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
    options.UseNpgsql(serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString)
           .UseSnakeCaseNamingConvention());

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, CookieJwtAuthenticationStateProvider>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.CanRead,
        policy => policy.RequireRole(Roles.Read, Roles.Manage, Roles.Admin));
    options.AddPolicy(AuthorizationPolicies.CanManage,
        policy => policy.RequireRole(Roles.Manage, Roles.Admin));
    options.AddPolicy(AuthorizationPolicies.CanAdmin,
        policy => policy.RequireRole(Roles.Admin));
});
// Authenticate by validating the JWT carried in the HttpOnly cookie, and on an
// unauthenticated challenge redirect the browser to the login page rather than
// returning 401.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = JwtTokenService.CreateValidationParameters(authSettings.Jwt);
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(authSettings.CookieName, out var token) && !string.IsNullOrEmpty(token))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
                context.Response.Redirect($"/login?returnUrl={returnUrl}");
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                context.Response.Redirect("/denied");
                return Task.CompletedTask;
            }
        };
    });

// --- Scheduled ingestion: rebuild the knowledge base every 6 hours. ---
builder.Services.AddQuartz(quartz =>
{
    var jobKey = new JobKey(nameof(IngestionJob));
    quartz.AddJob<IngestionJob>(options => options.WithIdentity(jobKey));
    quartz.AddTrigger(trigger => trigger
        .ForJob(jobKey)
        .WithIdentity($"{nameof(IngestionJob)}-trigger")
        .StartNow()
        .WithSimpleSchedule(schedule => schedule
            .WithIntervalInHours(6)
            .RepeatForever()));
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var app = builder.Build();

// Behind the Caddy TLS proxy the app receives plain HTTP; trust its
// X-Forwarded-* headers so Request.IsHttps/scheme (and thus Secure cookies and
// redirects) reflect the original HTTPS request. Only the proxy on the internal
// Docker network reaches the app, so all forwarders are trusted.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// One structured summary log per HTTP request (method, path, status, elapsed),
// enriched with the request id and signed-in user.
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestId", httpContext.TraceIdentifier);
        diagnosticContext.Set("User", httpContext.User.Identity?.Name ?? "anonymous");
    };
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

// After auth, tag every log written during the request with the request id and
// user, so individual events (not just the request summary) carry that context.
app.Use(async (httpContext, next) =>
{
    using (Serilog.Context.LogContext.PushProperty("RequestId", httpContext.TraceIdentifier))
    using (Serilog.Context.LogContext.PushProperty("User", httpContext.User.Identity?.Name ?? "anonymous"))
        await next();
});

app.UseAntiforgery();

app.MapStaticAssets();

// Serve the SVG logo for the conventional /favicon.ico probe so clients that
// request it by path (bots, older browsers) get 200 instead of 404.
app.MapMethods("/favicon.ico", ["GET", "HEAD"], (IWebHostEnvironment environment) =>
    Results.File(Path.Combine(environment.WebRootPath, "home_duties_assistant_ai_logo.svg"), "image/svg+xml"));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/logout", (HttpContext httpContext, IOptions<Auth> options) =>
{
    httpContext.Response.Cookies.Delete(options.Value.CookieName);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.Run();
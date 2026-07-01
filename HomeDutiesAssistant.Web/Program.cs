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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/logout", (HttpContext httpContext, IOptions<Auth> options) =>
{
    httpContext.Response.Cookies.Delete(options.Value.CookieName);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.Run();
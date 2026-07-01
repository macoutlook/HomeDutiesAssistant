using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HomeDutiesAssistant.Web.Data;

// Used only by `dotnet ef migrations` so the tool doesn't boot the app (which
// would run startup migration/seeding). Scaffolding needs the provider, not a
// live connection — the real connection string is configured in Program.cs.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }
}

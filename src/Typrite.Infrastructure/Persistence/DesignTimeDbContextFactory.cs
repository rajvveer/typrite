using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Typrite.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef</c> only. The connection string is irrelevant for generating migrations.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TypriteDbContext>
{
    public TypriteDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TypriteDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TYPRITE_PG") ?? "Host=localhost;Database=typrite;Username=postgres")
            .Options);
}

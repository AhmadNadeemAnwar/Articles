using FluentAssertions;
using Articles.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Articles.Tests.Persistence;

/// <summary>
/// The API applies its migrations when it starts, and EF Core refuses to start if the saved model
/// snapshot no longer matches the entities in the code (for example after a rename that missed a file).
/// These tests catch that here instead of in production. Neither opens a connection.
/// </summary>
public class MigrationSnapshotTests
{
    [Fact]
    public void The_sqlite_migrations_describe_the_current_model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new AppDbContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse("the migrations and their snapshot must match the entities");
    }

    [Fact]
    public void The_postgres_migrations_describe_the_current_model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused", npgsql => npgsql.MigrationsAssembly(DatabaseOptions.PostgresMigrationsAssembly))
            .Options;
        using var db = new AppDbContext(options);

        db.Database.HasPendingModelChanges().Should().BeFalse("production runs these migrations on start, so they must match the entities");
    }

    [Fact]
    public void The_postgres_migrations_are_found_in_the_assembly_the_app_names()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused", npgsql => npgsql.MigrationsAssembly(DatabaseOptions.PostgresMigrationsAssembly))
            .Options;
        using var db = new AppDbContext(options);

        db.Database.GetMigrations().Should().NotBeEmpty("a wrong assembly name would make production start with an empty database schema step");
    }
}

using Articles.Application.Common;
using Articles.Domain.Entities;
using Articles.Infrastructure.Persistence;
using Articles.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Articles.Tests;

/// <summary>
/// A real SQLite database held in memory for the lifetime of one test.
/// Using the actual provider rather than the in-memory one means these tests exercise the
/// LINQ-to-SQL translation, the value converters and the indexes that production uses.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public AppDbContext Db { get; }
    public PostRepository Posts { get; }
    public TagRepository Tags { get; }
    public UserRepository Users { get; }
    public EngagementRepository Engagement { get; }
    public CommentRepository Comments { get; }
    public AdminRepository Admin { get; }

    public TestDatabase()
    {
        // The database lives as long as the connection is open, so it must be held here.
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);

        Db.Database.EnsureCreated();

        Posts = new PostRepository(Db);
        Tags = new TagRepository(Db);
        Users = new UserRepository(Db);
        Engagement = new EngagementRepository(Db);
        Comments = new CommentRepository(Db);
        Admin = new AdminRepository(Db);
    }

    public async Task<User> AddUserAsync(string handle = "writer")
    {
        var user = new User($"{handle}@example.com", handle, handle.ToUpperInvariant(), "hash");
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    /// <summary>Builds the ProseMirror document shape the API stores, from plain paragraphs.</summary>
    public static string Document(params string[] paragraphs)
    {
        var nodes = paragraphs.Select(p =>
            $$"""{"type":"paragraph","content":[{"type":"text","text":{{System.Text.Json.JsonSerializer.Serialize(p)}}}]}""");

        return $$"""{"type":"doc","content":[{{string.Join(",", nodes)}}]}""";
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}

/// <summary>Password hashing stub: the real BCrypt work factor makes tests needlessly slow.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";
    public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    public string DummyHash => "hashed:dummy-" + Guid.NewGuid().ToString("N");
}

public sealed class FakePwnedPasswordChecker : IPwnedPasswordChecker
{
    public HashSet<string> Breached { get; } = new(StringComparer.Ordinal);
    public Task<bool> IsPwnedAsync(string password, CancellationToken ct = default) => Task.FromResult(Breached.Contains(password));
}

public sealed class FakeTurnstileVerifier : ITurnstileVerifier
{
    public bool IsEnabled { get; set; }
    public bool Passes { get; set; } = true;
    public string? LastToken { get; private set; }

    public Task<bool> VerifyAsync(string? token, CancellationToken ct = default)
    {
        LastToken = token;
        return Task.FromResult(Passes);
    }
}

/// <summary>A clock the test advances by hand.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

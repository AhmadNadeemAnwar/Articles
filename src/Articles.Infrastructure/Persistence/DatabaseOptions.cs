using Npgsql;

namespace Articles.Infrastructure.Persistence;

public enum DatabaseProvider
{
    Sqlite,
    Postgres
}

public static class DatabaseOptions
{
    public const string PostgresMigrationsAssembly = "Articles.Migrations.Postgres";

    public static DatabaseProvider ParseProvider(string? value) =>
        string.Equals(value, "postgres", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "postgresql", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProvider.Postgres
            : DatabaseProvider.Sqlite;

    /// <summary>
    /// Hosted Postgres providers (Neon, Render, Heroku) hand out a postgres:// URI, but Npgsql
    /// wants key=value pairs. Accepting the URI directly means the value can be pasted into an
    /// environment variable unchanged. Anything that is not a URI passes through untouched.
    /// </summary>
    public static string NormalisePostgresConnectionString(string connectionString)
    {
        var trimmed = connectionString.Trim();

        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var uri = new Uri(trimmed);
        var credentials = uri.UserInfo.Split(':', 2);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port < 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length > 1 ? Uri.UnescapeDataString(credentials[1]) : null,
            // Hosted providers only accept encrypted connections.
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}

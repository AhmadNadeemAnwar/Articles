using FluentAssertions;
using Articles.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Articles.Tests.Persistence;

public class DatabaseOptionsTests
{
    [Theory]
    [InlineData("Postgres", DatabaseProvider.Postgres)]
    [InlineData("postgresql", DatabaseProvider.Postgres)]
    [InlineData("POSTGRES", DatabaseProvider.Postgres)]
    [InlineData("Sqlite", DatabaseProvider.Sqlite)]
    [InlineData(null, DatabaseProvider.Sqlite)]
    [InlineData("anything-else", DatabaseProvider.Sqlite)]
    public void Provider_defaults_to_sqlite_unless_postgres_is_requested(string? value, DatabaseProvider expected) =>
        DatabaseOptions.ParseProvider(value).Should().Be(expected);

    [Fact]
    public void A_postgres_uri_is_converted_to_npgsql_key_value_form()
    {
        var result = DatabaseOptions.NormalisePostgresConnectionString(
            "postgresql://articles_owner:s3cr%40t@ep-cool-123.us-east-2.aws.neon.tech/articles?sslmode=require");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        parsed.Host.Should().Be("ep-cool-123.us-east-2.aws.neon.tech");
        parsed.Port.Should().Be(5432);
        parsed.Database.Should().Be("articles");
        parsed.Username.Should().Be("articles_owner");
        parsed.Password.Should().Be("s3cr@t", "percent-encoded characters in the URI must be decoded");
        parsed.SslMode.Should().Be(SslMode.Require);
    }

    [Fact]
    public void An_explicit_port_in_the_uri_is_respected()
    {
        var parsed = new NpgsqlConnectionStringBuilder(
            DatabaseOptions.NormalisePostgresConnectionString("postgres://u:p@host:6543/db"));

        parsed.Port.Should().Be(6543);
    }

    [Fact]
    public void A_key_value_connection_string_passes_through_unchanged()
    {
        const string original = "Host=localhost;Database=articles;Username=a;Password=b";

        DatabaseOptions.NormalisePostgresConnectionString(original).Should().Be(original);
    }
}

using Dapper;
using Microsoft.Data.SqlClient;
using SwiftBets.BuildingBlocks.Testing;

namespace SwiftBets.Casino.Infrastructure.Tests;

public sealed class MigratorTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Migrator_creates_the_casino_schema_and_rolls_back_and_reapplies()
    {
        var connectionString = await sql.CreateDatabaseAsync("mig_" + Guid.NewGuid().ToString("N")[..10]);
        string[] args = [$"--ConnectionStrings:SbCasino={connectionString}"];
        (await RunAsync(args)).ShouldBe(0);
        (await RunAsync(args)).ShouldBe(0);
        await using var connection = new SqlConnection(connectionString);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM casino.Providers WHERE Enabled = 1")).ShouldBe(3);

        await connection.ExecuteAsync(Rollback("0004_pragmatic"));
        await connection.ExecuteAsync(Rollback("0003_reconciliation"));
        await connection.ExecuteAsync(Rollback("0002_casino"));
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'casino'")).ShouldBe(0);

        (await RunAsync(args)).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM casino.Transactions")).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_connection_string_fails_with_a_usage_code() =>
        (await RunAsync([])).ShouldBe(2);

    private static string Rollback(string migration)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream($"SwiftBets.Casino.Migrator.Rollbacks.{migration}.sql")!;
        return new StreamReader(stream).ReadToEnd();
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var result = typeof(Program).Assembly.EntryPoint!.Invoke(null, [args]);
        return result is Task<int> task ? await task : (int)result!;
    }
}

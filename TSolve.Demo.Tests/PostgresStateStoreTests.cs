using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;
using TSolve.Demo.Services;

namespace TSolve.Demo.Tests;

public sealed class PostgresStateStoreTests
{
    [Fact]
    public void Migration_DefinesCoreSchemaConstraintsIndexesAndPgvector()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Database", "Migrations", "001_initial.sql");
        var sql = File.ReadAllText(path);
        foreach (var table in new[] { "resolved_ticket_snapshot", "evidence", "knowledge_candidate", "solution", "solution_source", "solution_approval", "reuse_record", "similarity_link", "ai_run", "audit_event", "cluster", "cluster_member" })
            Assert.Contains(table, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE (source, external_ticket_id)", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS vector", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING hnsw", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostgreSql_PersistsAcrossStoreInstances_AndSerializesConcurrentWrites_WhenConfigured()
    {
        var url = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url)) return;

        var options = Microsoft.Extensions.Options.Options.Create(new DatabaseOptions { Url = url });
        var first = new PostgresStateStore(options);
        await first.InitializeAsync();
        await first.ResetAsync();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(index => first.WriteAsync(state =>
        {
            state.Audit.Add(new AuditEntry { Action = "CONCURRENT_TEST", Resource = index.ToString(), Detail = "transactional write" });
            return true;
        })));

        var restarted = new PostgresStateStore(options);
        var records = await restarted.ReadAsync(state => state.Audit.Count(entry => entry.Action == "CONCURRENT_TEST"));
        Assert.Equal(2, records);
    }
}

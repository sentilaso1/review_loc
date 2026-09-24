using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TSolve.Demo.Models;
using TSolve.Demo.Options;

namespace TSolve.Demo.Services;

public sealed class PostgresStateStore : IStateStore
{
    private readonly Uri _databaseUri;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _psqlPath;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PostgresStateStore(IOptions<DatabaseOptions> options)
    {
        var value = Environment.GetEnvironmentVariable("DATABASE_URL") ?? options.Value.Url;
        if (!Uri.TryCreate(value, UriKind.Absolute, out _databaseUri!) ||
            (_databaseUri.Scheme != "postgresql" && _databaseUri.Scheme != "postgres"))
            throw new InvalidOperationException("DATABASE_URL must be a postgresql:// URI. PostgreSQL is authoritative; there is no in-memory fallback.");
        _psqlPath = ResolvePsql();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var probe = await RunPsqlAsync("SELECT 1;", _databaseUri, cancellationToken, throwOnError: false);
        if (probe.ExitCode != 0 && probe.Error.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            var database = _databaseUri.AbsolutePath.Trim('/');
            if (!Regex.IsMatch(database, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                throw new InvalidOperationException("Database name in DATABASE_URL is not a safe PostgreSQL identifier.");
            var maintenance = new UriBuilder(_databaseUri) { Path = "/postgres" }.Uri;
            await RunPsqlAsync($"CREATE DATABASE \"{database}\";", maintenance, cancellationToken);
        }
        else if (probe.ExitCode != 0)
            throw new InvalidOperationException($"Cannot connect to PostgreSQL using DATABASE_URL: {probe.Error.Trim()}");

        var migrationPath = Path.Combine(AppContext.BaseDirectory, "Database", "Migrations", "001_initial.sql");
        if (!File.Exists(migrationPath)) throw new FileNotFoundException("PostgreSQL migration was not deployed.", migrationPath);
        await RunPsqlAsync(await File.ReadAllTextAsync(migrationPath, cancellationToken), _databaseUri, cancellationToken);
    }

    public async Task<T> ReadAsync<T>(Func<DemoState, T> reader)
    {
        await _gate.WaitAsync();
        try { return reader(await LoadAsync()); }
        finally { _gate.Release(); }
    }

    public async Task<T> WriteAsync<T>(Func<DemoState, T> writer)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await LoadAsync();
            var result = writer(state);
            await SaveAsync(state);
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task ResetAsync() => WriteAsync(state =>
    {
        state.Tickets.Clear(); state.Clusters.Clear(); state.Solutions.Clear(); state.Reviews.Clear();
        state.SimilarityLinks.Clear(); state.AIRuns.Clear(); state.Runs.Clear(); state.Audit.Clear();
        state.DailySyncNumber = 0;
        return true;
    });

    private async Task<DemoState> LoadAsync()
    {
        var output = await RunPsqlAsync("SELECT payload::text FROM tsolve_state_metadata WHERE singleton;", _databaseUri, CancellationToken.None);
        return string.IsNullOrWhiteSpace(output.Output) || output.Output.Trim() == "{}"
            ? new DemoState()
            : JsonSerializer.Deserialize<DemoState>(output.Output.Trim(), Json) ?? new DemoState();
    }

    private async Task SaveAsync(DemoState state)
    {
        var sql = new StringBuilder("BEGIN; SELECT pg_advisory_xact_lock(846765021); ");
        sql.Append("TRUNCATE evidence, knowledge_candidate, cluster_member, solution_source, reuse_record, similarity_link, solution_approval, ai_run, audit_event, pipeline_run, solution, \"cluster\", resolved_ticket_snapshot RESTART IDENTITY CASCADE; ");
        foreach (var ticket in state.Tickets)
        {
            sql.Append($"INSERT INTO resolved_ticket_snapshot(id,source,external_ticket_id,workspace_id,category,status,decision,cluster_id,payload) VALUES({U(ticket.Id)},{Q(ticket.Source)},{Q(ticket.ExternalId)},{Q(ticket.Workspace)},{Q(ticket.Category)},{Q(ticket.Status)},{Q(ticket.Decision.ToString())},{UN(ticket.ClusterId)},{J(ticket)}); ");
            if (!string.IsNullOrWhiteSpace(ticket.CleanResolution))
                sql.Append($"INSERT INTO evidence(id,ticket_id,kind,content) VALUES({U(Guid.NewGuid())},{U(ticket.Id)},'clean_resolution',{Q(ticket.CleanResolution)}); ");
            if (ticket.Decision == ProcessingDecision.CandidateEligible)
                sql.Append($"INSERT INTO knowledge_candidate(id,ticket_id,workspace_id,category,decision,quality_score,risk) VALUES({U(Guid.NewGuid())},{U(ticket.Id)},{Q(ticket.Workspace)},{Q(ticket.Category)},{Q(ticket.Decision.ToString())},{ticket.QualityScore},{Q(ticket.Risk.ToString())}); ");
        }
        foreach (var cluster in state.Clusters)
        {
            sql.Append($"INSERT INTO \"cluster\"(id,workspace_id,category,subcategory,status,payload) VALUES({U(cluster.Id)},{Q(cluster.Workspace)},{Q(cluster.Category)},{Q(cluster.Subcategory)},{Q(cluster.Promoted ? "PROMOTED" : "CANDIDATE")},{J(cluster)}); ");
            foreach (var ticketId in cluster.TicketIds) sql.Append($"INSERT INTO cluster_member(cluster_id,ticket_id) VALUES({U(cluster.Id)},{U(ticketId)}); ");
        }
        foreach (var solution in state.Solutions)
        {
            sql.Append($"INSERT INTO solution(id,cluster_id,workspace_id,status,payload) VALUES({U(solution.Id)},{U(solution.ClusterId)},{Q(solution.Workspace)},{Q(solution.Status.ToString())},{J(solution)}); ");
            foreach (var ticketId in solution.SourceTicketIds) sql.Append($"INSERT INTO solution_source(solution_id,ticket_id) VALUES({U(solution.Id)},{U(ticketId)}); ");
        }
        foreach (var review in state.Reviews) sql.Append($"INSERT INTO solution_approval(id,solution_id,decision,payload) VALUES({U(review.Id)},{U(review.SolutionId)},{Q(review.Decision.ToString())},{J(review)}); ");
        foreach (var link in state.SimilarityLinks) sql.Append($"INSERT INTO similarity_link(id,source_ticket_id,target_ticket_id,score,payload) VALUES({U(link.Id)},{U(link.SourceTicketId)},{U(link.TargetTicketId)},{D(link.Score)},{J(link)}); ");
        foreach (var ticket in state.Tickets.Where(ticket => ticket.Decision == ProcessingDecision.AutoLinked && ticket.LinkedSolutionId is not null))
            sql.Append($"INSERT INTO reuse_record(id,solution_id,ticket_id,confidence) VALUES({U(Guid.NewGuid())},{U(ticket.LinkedSolutionId!.Value)},{U(ticket.Id)},{D(ticket.MatchConfidence)}); ");
        foreach (var run in state.AIRuns) sql.Append($"INSERT INTO ai_run(id,cluster_id,model,prompt_hash,latency_ms,cost,payload) VALUES({U(run.Id)},{U(run.ClusterId)},{Q(run.Model)},{Q(run.PromptHash)},{run.LatencyMs},{run.Cost.ToString(CultureInfo.InvariantCulture)},{J(run)}); ");
        foreach (var audit in state.Audit) sql.Append($"INSERT INTO audit_event(action,resource,at,payload) VALUES({Q(audit.Action)},{Q(audit.Resource)},{Q(audit.At.ToString("O"))}::timestamptz,{J(audit)}); ");
        foreach (var run in state.Runs) sql.Append($"INSERT INTO pipeline_run(id,status,queue_name,payload) VALUES({U(run.Id)},{Q(run.CompletedAt == default ? "RUNNING" : "COMPLETED")},{Q(run.QueueName)},{J(run)}); ");
        sql.Append($"UPDATE tsolve_state_metadata SET daily_sync_number={state.DailySyncNumber}, payload={J(state)} WHERE singleton; COMMIT;");
        await RunPsqlAsync(sql.ToString(), _databaseUri, CancellationToken.None);
    }

    private async Task<PsqlResult> RunPsqlAsync(string sql, Uri uri, CancellationToken cancellationToken, bool throwOnError = true)
    {
        var credentials = uri.UserInfo.Split(':', 2);
        var start = new ProcessStartInfo(_psqlPath)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("-X"); start.ArgumentList.Add("-q"); start.ArgumentList.Add("-t"); start.ArgumentList.Add("-A");
        start.ArgumentList.Add("-v"); start.ArgumentList.Add("ON_ERROR_STOP=1");
        start.ArgumentList.Add("--host"); start.ArgumentList.Add(uri.Host);
        start.ArgumentList.Add("--port"); start.ArgumentList.Add((uri.IsDefaultPort ? 5432 : uri.Port).ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--username"); start.ArgumentList.Add(Uri.UnescapeDataString(credentials[0]));
        start.ArgumentList.Add("--dbname"); start.ArgumentList.Add(uri.AbsolutePath.Trim('/'));
        if (credentials.Length > 1) start.Environment["PGPASSWORD"] = Uri.UnescapeDataString(credentials[1]);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start psql.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(sql.AsMemory(), cancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        var result = new PsqlResult(process.ExitCode, await stdout, await stderr);
        if (throwOnError && result.ExitCode != 0) throw new InvalidOperationException($"PostgreSQL operation failed: {result.Error.Trim()}");
        return result;
    }

    private static string ResolvePsql()
    {
        var configured = Environment.GetEnvironmentVariable("PSQL_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var root = Path.Combine(programFiles, "PostgreSQL");
        var found = Directory.Exists(root) ? Directory.GetDirectories(root).OrderByDescending(path => path).Select(path => Path.Combine(path, "bin", "psql.exe")).FirstOrDefault(File.Exists) : null;
        return found ?? throw new InvalidOperationException("psql was not found. Install PostgreSQL client tools or set PSQL_PATH.");
    }

    private static string Q(string value) => $"'{value.Replace("'", "''")}'";
    private static string J<T>(T value) => Q(JsonSerializer.Serialize(value, Json)) + "::jsonb";
    private static string U(Guid value) => Q(value.ToString()) + "::uuid";
    private static string UN(Guid? value) => value is null ? "NULL" : U(value.Value);
    private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private sealed record PsqlResult(int ExitCode, string Output, string Error);
}

public sealed class DatabaseInitializer(PostgresStateStore store, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        logger.LogInformation("PostgreSQL schema is ready; persistent T-Solve state enabled.");
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

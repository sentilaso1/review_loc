using TSolve.Demo.Options;
using TSolve.Demo.Services;
using TSolve.Demo.Services.Jira;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection(JiraOptions.Section));
builder.Services.Configure<PipelineOptions>(builder.Configuration.GetSection(PipelineOptions.Section));
builder.Services.Configure<SynthesisOptions>(builder.Configuration.GetSection(SynthesisOptions.Section));
builder.Services.Configure<DatabaseOptions>(options =>
{
    options.Url = builder.Configuration["Database:Url"];
    options.MigrateOnStartup = builder.Configuration.GetValue("Database:MigrateOnStartup", true);
});
builder.Services.AddSingleton<PostgresStateStore>();
builder.Services.AddSingleton<IStateStore>(provider => provider.GetRequiredService<PostgresStateStore>());
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<TextProcessingService>();
builder.Services.AddSingleton<ISynthesisRewriter, InternalSafeRewriter>();
builder.Services.AddSingleton<TwoStageSynthesisService>();
builder.Services.AddSingleton<ExcelTicketReader>();
builder.Services.AddSingleton<MockJiraClient>();
builder.Services.AddHttpClient<RealJiraClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<JiraClientSelector>();
builder.Services.AddSingleton<KnowledgePipelineService>();

if (args.Length >= 3 && args[0].Equals("--backfill-report", StringComparison.OrdinalIgnoreCase))
{
    var inputPath = Path.GetFullPath(args[1]);
    var outputPath = Path.GetFullPath(args[2]);
    var jira = Microsoft.Extensions.Options.Options.Create(new JiraOptions { Mode = "Mock" });
    var pipelineOptions = Microsoft.Extensions.Options.Options.Create(
        builder.Configuration.GetSection(PipelineOptions.Section).Get<PipelineOptions>() ?? new PipelineOptions());
    var flags = args.Skip(3).Select(value => value.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    IStateStore store;
    if (flags.Contains("postgres"))
    {
        var postgres = new PostgresStateStore(Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
        {
            Url = Environment.GetEnvironmentVariable("DATABASE_URL")
        }));
        await postgres.InitializeAsync();
        await postgres.ResetAsync();
        store = postgres;
    }
    else store = new DemoStore();
    var text = new TextProcessingService(pipelineOptions);
    var selector = new JiraClientSelector(new MockJiraClient(), new RealJiraClient(new HttpClient(), jira), jira);
    var pipeline = new KnowledgePipelineService(store, text, selector, jira,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<KnowledgePipelineService>.Instance, pipelineOptions);
    IReadOnlyList<TSolve.Demo.Models.SourceTicket> tickets;
    await using (var input = File.OpenRead(inputPath)) tickets = new ExcelTicketReader().Read(input, inputPath);
    if (flags.Contains("reverse")) tickets = tickets.Reverse().ToArray();
    else if (flags.Contains("shuffle")) tickets = tickets.OrderBy(ticket => text.Hash(ticket.ExternalId), StringComparer.Ordinal).ToArray();
    var run = await pipeline.ImportExcelAsync(tickets, inputPath, CancellationToken.None);
    var state = await store.ReadAsync(value => value);
    await using (var output = File.Create(outputPath)) new PipelineReportWriter().Write(output, state, run);
    Console.WriteLine($"Report written to {outputPath}; tickets={state.Tickets.Count}, clusters={state.Clusters.Count}, drafts={state.Solutions.Count}.");
    return;
}

var app = builder.Build();

if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error");
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.Run();
